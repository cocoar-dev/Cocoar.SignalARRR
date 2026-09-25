import type * as signalR from '@microsoft/signalr';

/** A credential: a fixed value, or a function called whenever the credential is needed. */
export type Credential = string | (() => string | Promise<string>);

/**
 * SignalARRR's options for a connection.
 *
 * A connection has two credentials, checked by different things. The **connection credential**
 * authenticates negotiate and the transport and is what `[Authorize]` on the hub class checks. The
 * **message credential** travels with every message, answers authentication challenges and
 * authenticates file transfers, and is what `[Authorize]` on a method or a `ServerMethods` class
 * checks. The options are named the same in every SignalARRR client, and nothing is coupled
 * implicitly: setting one of the two sets only that one.
 */
export class HARRRConnectionOptions {
  /**
   * One credential for the connection and for every message — the common case. A
   * `connectionCredential` or `messageCredential` set alongside it takes its part over.
   */
  public credential?: Credential;

  /**
   * Authenticates the connection: the negotiate request and the transport. Becomes SignalR's
   * `accessTokenFactory`, so it is called on every connect and reconnect. Only available through
   * `HARRRConnection.create(url, options)`; a `HubConnection` that is already built has its own.
   */
  public connectionCredential?: Credential;

  /**
   * The credential SignalARRR sends with every message, as the answer to an authentication
   * challenge, and on file-transfer requests. Resolved per use, so a refreshed token is picked up.
   */
  public messageCredential?: Credential;

  /**
   * SignalR's own connection options for `HARRRConnection.create(url, options)` — transport,
   * headers, `withCredentials` and the like. Set the connection credential through
   * `connectionCredential` or `credential`, not through `accessTokenFactory` here.
   */
  public httpConnectionOptions?: signalR.IHttpConnectionOptions;

  /**
   * The message credential under its former name.
   *
   * SignalARRR used to adopt SignalR's factory by reading private fields off the connection, so the
   * two could never be told apart, and a credential meant for the connection alone — a single-use
   * ticket, say — was resent with every message. Nothing is adopted now: a connection that
   * authenticates per message has to say so, or the server will challenge it for a credential it
   * never sends once the auth cache expires.
   *
   * @deprecated Use `messageCredential`, or `credential` for one credential covering the connection
   * and every message.
   */
  public authorization?: Credential;
}

/** Turns a credential option into the function SignalARRR and SignalR call. */
export function credentialFactory(credential: Credential): () => string | Promise<string> {
  return typeof credential === 'function' ? credential : () => credential;
}

/** The message credential in effect, after checking that the old and the new option are not both set. */
export function resolveMessageCredential(options?: HARRRConnectionOptions): Credential | undefined {
  // The former option is still honoured.
  const legacy = options?.authorization;
  const current = options?.messageCredential ?? options?.credential;
  if (legacy !== undefined && current !== undefined) {
    throw new Error(
      'Both authorization and messageCredential/credential are set. authorization is the former name of messageCredential; set only messageCredential (or credential).',
    );
  }
  return legacy ?? current;
}

/** The connection credential in effect. */
export function resolveConnectionCredential(options?: HARRRConnectionOptions): Credential | undefined {
  return options?.connectionCredential ?? options?.credential;
}
