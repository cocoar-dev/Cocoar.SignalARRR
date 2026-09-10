# Cocoar.SignalARRR — Kotlin client

The SignalARRR client for Android and the JVM. Documentation: <https://docs.cocoar.dev/signalarrr/guide/kotlin-client/setup>

| Module | Artifact | Purpose |
|---|---|---|
| `signalarrr` | `dev.cocoar:signalarrr` | Runtime: `HARRRConnection`, the plain `SignalRClient`, transports, JSON and MessagePack protocols |
| `signalarrr-ksp` | `dev.cocoar:signalarrr-ksp` | KSP processor generating `<Name>Proxy` classes from `@HubProxy` interfaces |
| `signalarrr-integration-tests` | — | Tests against the shared `IntegrationTestServer`; skipped unless `SIGNALARRR_TEST_SERVER_URL` is set |

## Building

Needs a JDK 21 on `JAVA_HOME` (the library itself targets Java 8 bytecode).

```bash
./gradlew build                                   # unit tests + KSP processor
../../scripts/run-integration-tests.sh kotlin     # integration tests, starts the .NET test server
./gradlew publishToMavenLocal -Pversion=5.2.0     # local consumption from an app
```

## Layout

```
signalarrr/src/main/kotlin/dev/cocoar/signalarrr/
  SignalRClient.kt        negotiate, transport selection, handshake, receive loop, ping, server timeout, reconnect
  HARRRConnection.kt      the SignalARRR protocol: envelopes, token challenge, server→client dispatch, stream references
  transport/              WebSocket (OkHttp), Server-Sent Events, Long Polling
  protocol/               JSON and MessagePack hub protocols; messages are carried as kotlinx JsonElement
  serialization/          JsonValues — dynamic encode/decode between Kotlin values and JsonElement
  CancellationManager.kt  server-cancellable handler coroutines
  HARRRError.kt           the structured error envelope and its codes
```
