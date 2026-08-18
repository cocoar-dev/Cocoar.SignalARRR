import * as signalR from '@microsoft/signalr';
import { ClientRequestMessage } from './models/client-request-message.js';
import { ServerRequestMessage } from './models/server-request-message.js';
import { asCancellationTokenReference } from './models/cancellation-token-reference.js';
import { isStreamReference, resolveStreamReference } from './models/stream-reference.js';
import { parseHARRRError } from './models/harrr-error.js';
import { HARRRConnectionOptions } from './harrr-connection-options.js';
import { CancellationManager } from './cancellation-manager.js';

export class HARRRConnection {
  private _hubConnection: signalR.HubConnection;
  // SignalR's contract is `() => string | Promise<string>`, and every OAuth-backed application
  // returns the promise. Typing the field as synchronous did not make it so: the promise itself was
  // serialised into `ClientRequestMessage.Authorization`, arrived as `{}`, and the server — where
  // the field is a `string` — failed to bind the whole message. Every invoke, send and stream on
  // that connection died with "Error binding arguments", while server-to-client calls kept working
  // because they carry no such message. Both .NET clients and the Swift client await the token on
  // every send path; this one now does too.
  private _accessTokenFactory: () => string | Promise<string> = () => '';
  private _serverRequestHandlers = new Map<string, (...args: unknown[]) => unknown>();
  private _serverStreamHandlers = new Map<string, (...args: unknown[]) => AsyncIterable<unknown>>();
  private _cancellationManager = new CancellationManager();

  public get baseUrl(): string {
    return this._hubConnection.baseUrl;
  }

  public set baseUrl(value: string) {
    this._hubConnection.baseUrl = value;
  }

  public get connectionId(): string | null {
    return this._hubConnection.connectionId;
  }

  public get state(): signalR.HubConnectionState {
    return this._hubConnection.state;
  }

  public get serverTimeoutInMilliseconds(): number {
    return this._hubConnection.serverTimeoutInMilliseconds;
  }

  public set serverTimeoutInMilliseconds(value: number) {
    this._hubConnection.serverTimeoutInMilliseconds = value;
  }

  public get keepAliveIntervalInMilliseconds(): number {
    return this._hubConnection.keepAliveIntervalInMilliseconds;
  }

  public set keepAliveIntervalInMilliseconds(value: number) {
    this._hubConnection.keepAliveIntervalInMilliseconds = value;
  }

  constructor(hubConnection: signalR.HubConnection, _options?: HARRRConnectionOptions) {
    this._hubConnection = hubConnection;

    const conn = (hubConnection as unknown as Record<string, unknown>)['connection'] as Record<string, unknown> | undefined;
    const factory =
      (conn?.['_options'] as Record<string, unknown> | undefined)?.['accessTokenFactory'] ??
      conn?.['_accessTokenFactory'];
    if (typeof factory === 'function') {
      this._accessTokenFactory = factory as () => string | Promise<string>;
    }

    // Native client results — return values are sent back to the server automatically by SignalR.
    // A promise is fine here without awaiting it ourselves: SignalR awaits the return value of a
    // client-result handler before completing the invocation.
    this._hubConnection.on('ChallengeAuthentication', (req: ServerRequestMessage) => {
      return this._accessTokenFactory();
    });

    this._hubConnection.on('InvokeServerRequest', async (req: ServerRequestMessage) => {
      // If StreamId is present, stream results back instead of returning a single value
      if (req.StreamId) {
        await this._streamBackToServer(req, req.StreamId);
        return undefined;
      }
      const result = await this._dispatchServerMethod(req);

      // If the result is binary data (Blob, ArrayBuffer, Buffer), upload via HTTP
      // and return a StreamReference instead
      if (result instanceof Blob || result instanceof ArrayBuffer ||
          isNodeBuffer(result)) {
        return await this._uploadAndReturnReference(result);
      }

      return result;
    });

    this._hubConnection.on('InvokeServerMessage', async (req: ServerRequestMessage) => {
      try {
        if (req.StreamId) {
          await this._streamBackToServer(req, req.StreamId);
        } else {
          await this._dispatchServerMethod(req);
        }
      } catch (err) {
        console.error(`[SignalARRR] Failed to handle server message '${req.Method}':`, err);
      }
    });

    this._hubConnection.on('CancelTokenFromServer', (req: ServerRequestMessage) => {
      if (req.CancellationGuid) {
        this._cancellationManager.cancel(req.CancellationGuid);
      }
    });
  }

  private async _dispatchServerMethod(req: ServerRequestMessage): Promise<unknown> {
    const handler = this._serverRequestHandlers.get(req.Method);
    if (!handler) return undefined;

    const args = await this._prepareArgs(req);
    return await handler(...args);
  }

  private async _prepareArgs(req: ServerRequestMessage): Promise<unknown[]> {
    const args: unknown[] = [];
    for (const arg of req.Arguments ?? []) {
      const cancellationRef = asCancellationTokenReference(arg);
      if (cancellationRef) {
        // Keyed on the reference's own id, not on the request's CancellationGuid. Those are two
        // different things: the guid cancels the call, the reference cancels this one parameter.
        // Registering under the guid meant a cancellation aimed at a parameter never found its
        // token here, and two token parameters could not be cancelled apart.
        args.push(this._cancellationManager.create(cancellationRef.Id));
      } else if (isStreamReference(arg)) {
        // Download the stream data via HTTP and pass as ArrayBuffer
        args.push(await resolveStreamReference(arg));
      } else {
        args.push(arg);
      }
    }
    return args;
  }

  private async _streamBackToServer(req: ServerRequestMessage, streamId: string): Promise<void> {
    const args = await this._prepareArgs(req);

    try {
      // Try stream handler first
      const streamHandler = this._serverStreamHandlers.get(req.Method);
      if (streamHandler) {
        const stream = streamHandler(...args);
        for await (const item of stream) {
          await this._hubConnection.send('StreamItemToServer', streamId, item);
        }
        await this._hubConnection.send('StreamCompleteToServer', streamId, null);
        return;
      }

      // Fall back to regular handler — send single result as one item
      const handler = this._serverRequestHandlers.get(req.Method);
      if (handler) {
        const result = await handler(...args);
        if (result != null) {
          // Check if result is async iterable
          if (typeof result === 'object' && Symbol.asyncIterator in (result as object)) {
            for await (const item of result as AsyncIterable<unknown>) {
              await this._hubConnection.send('StreamItemToServer', streamId, item);
            }
          } else {
            await this._hubConnection.send('StreamItemToServer', streamId, result);
          }
        }
        await this._hubConnection.send('StreamCompleteToServer', streamId, null);
      } else {
        await this._hubConnection.send('StreamCompleteToServer', streamId, null);
      }
    } catch (err) {
      await this._hubConnection.send('StreamCompleteToServer', streamId, String(err));
    }
  }

  private async _uploadAndReturnReference(data: Blob | ArrayBuffer | Uint8Array | unknown): Promise<{ Uri: string }> {
    // Request an upload URL from the server
    const uploadUrl = await this._hubConnection.invoke<string>('RequestUploadSlot');

    // Upload the data via HTTP POST
    let body: BodyInit;
    if (data instanceof Blob) {
      body = data;
    } else if (data instanceof ArrayBuffer || data instanceof Uint8Array) {
      body = data as BodyInit;
    } else {
      body = String(data);
    }
    await fetch(uploadUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'application/octet-stream' },
      body,
    });

    return { Uri: uploadUrl };
  }

  /** Prepare outgoing arguments — upload binary data and replace with StreamReferences. */
  private async _prepareOutgoingArgs(args: unknown[]): Promise<unknown[]> {
    let hasStream = false;
    for (const arg of args) {
      if (arg instanceof Blob || arg instanceof ArrayBuffer ||
          isNodeBuffer(arg)) {
        hasStream = true;
        break;
      }
    }
    if (!hasStream) return args;

    const result: unknown[] = [];
    for (const arg of args) {
      if (arg instanceof Blob || arg instanceof ArrayBuffer ||
          isNodeBuffer(arg)) {
        result.push(await this._uploadAndReturnReference(arg));
      } else {
        result.push(arg);
      }
    }
    return result;
  }

  public start(): Promise<void> {
    return this._hubConnection.start();
  }

  public stop(): Promise<void> {
    return this._hubConnection.stop();
  }

  public onClose(callback: (error?: Error) => void): void {
    this._hubConnection.onclose(callback);
  }

  public onReconnecting(callback: (error?: Error) => void): void {
    this._hubConnection.onreconnecting(callback);
  }

  public onReconnected(callback: (connectionId?: string) => void): void {
    this._hubConnection.onreconnected(callback);
  }

  /** Resolves the token factory, sync or async, to the string the message field expects. */
  private async _resolveAuthorization(): Promise<string> {
    return (await this._accessTokenFactory()) ?? '';
  }

  private async _buildRequest(methodName: string, args: unknown[]): Promise<ClientRequestMessage> {
    const preparedArgs = await this._prepareOutgoingArgs(args);
    return {
      Method: methodName,
      Arguments: preparedArgs,
      Authorization: await this._resolveAuthorization(),
    };
  }

  public async invoke<T>(methodName: string, ...args: unknown[]): Promise<T> {
    const msg = await this._buildRequest(methodName, args);
    return this._hubConnection
      .invoke<T>('InvokeMessageResult', msg)
      .catch(err => Promise.reject(this._extractException(err)));
  }

  public async send(methodName: string, ...args: unknown[]): Promise<void> {
    const msg = await this._buildRequest(methodName, args);
    return this._hubConnection.send('SendMessage', msg);
  }

  /**
   * `IStreamResult` has to be handed back synchronously, but building the message is asynchronous —
   * the token has to be awaited, and binary arguments have to be uploaded first, which this path
   * used to skip entirely. The result is therefore deferred: it starts the real stream once the
   * message is ready and forwards to whoever subscribed in the meantime.
   */
  public stream<T>(methodName: string, ...args: unknown[]): signalR.IStreamResult<T> {
    const pending = this._buildRequest(methodName, args).then(msg =>
      this._hubConnection.stream<T>('StreamMessage', msg),
    );
    return new DeferredStreamResult<T>(pending);
  }

  public on(methodName: string, newMethod: (...args: any[]) => void): void {
    this._hubConnection.on(methodName, newMethod);
  }

  /** Register a handler for server-to-client method calls (single return value). */
  public onServerMethod(methodName: string, func: (...args: unknown[]) => unknown): this {
    this._serverRequestHandlers.set(methodName, func);
    return this;
  }

  /** Register a handler for server-to-client streaming calls (returns AsyncIterable). */
  public onServerStreamMethod(methodName: string, func: (...args: unknown[]) => AsyncIterable<unknown>): this {
    this._serverStreamHandlers.set(methodName, func);
    return this;
  }

  public off(methodName: string): void;
  public off(methodName: string, method: (...args: any[]) => void): void;
  public off(methodName: string, method?: (...args: any[]) => void): void {
    if (!method) {
      this._hubConnection.off(methodName);
    } else {
      this._hubConnection.off(methodName, method);
    }
  }

  public asSignalRHubConnection(): signalR.HubConnection {
    return this._hubConnection;
  }

  public static create(
    hubConnection: signalR.HubConnection | ((builder: signalR.HubConnectionBuilder) => void),
    options?: HARRRConnectionOptions,
  ): HARRRConnection {
    if (hubConnection instanceof Function) {
      const builder = new signalR.HubConnectionBuilder();
      hubConnection(builder);
      return new HARRRConnection(builder.build(), options);
    }
    return new HARRRConnection(hubConnection, options);
  }

  private _extractException(error: unknown): { type: string; message: string } {
    const parsed = parseHARRRError(error);
    return { type: parsed.Type, message: parsed.Message };
  }
}

/**
 * An `IStreamResult` whose underlying stream is not open yet.
 *
 * Subscribers registered before the stream exists are attached as soon as it does; a failure while
 * preparing the message — a rejecting token factory, a failed upload — reaches them through
 * `error()` rather than being thrown out of `stream()`, which is where a caller can observe it.
 * Disposing before the stream opens means it is never subscribed to at all.
 */
class DeferredStreamResult<T> implements signalR.IStreamResult<T> {
  constructor(private readonly _pending: Promise<signalR.IStreamResult<T>>) {
    // The rejection is delivered to subscribers below. This keeps it from also counting as an
    // unhandled rejection when nobody subscribes, which in Node terminates the process.
    this._pending.catch(() => undefined);
  }

  public subscribe(subscriber: signalR.IStreamSubscriber<T>): signalR.ISubscription<T> {
    let inner: signalR.ISubscription<T> | undefined;
    let disposed = false;

    this._pending.then(
      stream => {
        if (disposed) return;
        inner = stream.subscribe(subscriber);
      },
      err => {
        if (disposed) return;
        subscriber.error(err);
      },
    );

    return {
      dispose: () => {
        disposed = true;
        inner?.dispose();
      },
    };
  }
}

/** Runtime check for Node.js Buffer without importing @types/node */
function isNodeBuffer(value: unknown): boolean {
  return typeof globalThis !== 'undefined' &&
    typeof (globalThis as Record<string, unknown>)['Buffer'] === 'function' &&
    typeof ((globalThis as Record<string, unknown>)['Buffer'] as Record<string, unknown>)['isBuffer'] === 'function' &&
    ((globalThis as Record<string, unknown>)['Buffer'] as { isBuffer: (v: unknown) => boolean }).isBuffer(value);
}
