export class HARRRConnectionOptions {
  /**
   * The credential SignalARRR sends: the `Authorization` field of every message, the answer to an
   * authentication challenge, and the file-transfer requests.
   *
   * This is a different thing from SignalR's own `accessTokenFactory`, which authenticates the
   * connection — the negotiate request and the transport. The two are checked by different things:
   * SignalR's by `[Authorize]` on the hub class, this one by `[Authorize]` on a method or a
   * `ServerMethods` class. Pass the same factory to both when it is the same credential, which is
   * the common case.
   *
   * SignalARRR used to adopt SignalR's factory by reading private fields off the connection, so the
   * two could never be told apart, and a credential meant for the connection alone — a single-use
   * ticket, say — was resent with every message. Nothing is adopted now: a connection that
   * authenticates per message has to say so here, or the server will challenge it for a credential
   * it never sends once the auth cache expires.
   */
  public authorization?: (() => string | Promise<string>) | string;
}
