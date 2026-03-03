import * as signalR from '@microsoft/signalr';
import { ClientRequestMessage } from './models/client-request-message.js';
import { ServerRequestMessage } from './models/server-request-message.js';
import { isCancellationTokenReference } from './models/cancellation-token-reference.js';
import { HARRRConnectionOptions } from './harrr-connection-options.js';
import { CancellationManager } from './cancellation-manager.js';

export class HARRRConnection {
  private _hubConnection: signalR.HubConnection;
  private _accessTokenFactory: () => string = () => '';
  private _serverRequestHandlers = new Map<string, (...args: unknown[]) => unknown>();
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
      this._accessTokenFactory = factory as () => string;
    }

    this._hubConnection.on('ChallengeAuthentication', async (req: ServerRequestMessage) => {
      const token = this._accessTokenFactory();
      await this._hubConnection.send('ReplyServerRequest', req.Id, token, null);
    });

    this._hubConnection.on('InvokeServerRequest', async (req: ServerRequestMessage) => {
      let payload: unknown = null;
      let error: string | null = null;
      try {
        payload = await this._dispatchServerMethod(req);
      } catch (e) {
        error = String(e);
      }
      await this._hubConnection.send('ReplyServerRequest', req.Id, payload, error);
    });

    this._hubConnection.on('InvokeServerMessage', async (req: ServerRequestMessage) => {
      try {
        await this._dispatchServerMethod(req);
      } catch {
        // ignored — fire-and-forget
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

    const args = (req.Arguments ?? []).map(arg => {
      if (isCancellationTokenReference(arg) && req.CancellationGuid) {
        return this._cancellationManager.create(req.CancellationGuid);
      }
      return arg;
    });

    return await handler(...args);
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

  public invoke<T>(methodName: string, ...args: unknown[]): Promise<T> {
    const msg: ClientRequestMessage = {
      Method: methodName,
      Arguments: args,
      Authorization: this._accessTokenFactory(),
    };
    return this._hubConnection
      .invoke<T>('InvokeMessageResult', msg)
      .catch(err => Promise.reject(this._extractException(err)));
  }

  public send(methodName: string, ...args: unknown[]): Promise<void> {
    const msg: ClientRequestMessage = {
      Method: methodName,
      Arguments: args,
      Authorization: this._accessTokenFactory(),
    };
    return this._hubConnection.send('InvokeMessage', msg);
  }

  public stream<T>(methodName: string, ...args: unknown[]): signalR.IStreamResult<T> {
    const msg: ClientRequestMessage = {
      Method: methodName,
      Arguments: args,
      Authorization: this._accessTokenFactory(),
    };
    return this._hubConnection.stream<T>('StreamMessage', msg);
  }

  public on(methodName: string, newMethod: (...args: any[]) => void): void {
    this._hubConnection.on(methodName, newMethod);
  }

  public onServerMethod(methodName: string, func: (...args: unknown[]) => unknown): this {
    this._serverRequestHandlers.set(methodName, func);
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
    const msg = error instanceof Error ? error.message : String(error);
    const matches = /.*\[(.*)\]\s*(.*)/m.exec(msg);
    return {
      type: matches?.[1] ?? 'Error',
      message: matches?.[2] ?? msg,
    };
  }
}
