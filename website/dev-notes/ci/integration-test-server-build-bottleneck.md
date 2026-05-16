# CI Build Bottleneck: IntegrationTestServer nested build

## Summary

We hit a severe CI regression where Windows and Ubuntu test jobs jumped from roughly a minute to roughly 16 minutes. The root cause was **not** SignalR connect/disconnect logic or the new backplane tests themselves.

The slowdown came from a nested build inside the integration test fixture:

- `IntegrationTestServerBuildCoordinator.EnsureBuilt(...)`
- called from the test fixture before launching `IntegrationTestServer`

Diagnostics showed that this explicit build step alone consumed roughly **911-913 seconds** on GitHub Actions Windows and Ubuntu runners.

## What happened

We had introduced a safety mechanism so the integration test fixture could launch the test server with `dotnet run --no-build` after forcing a prior build. That looked reasonable, but it moved an explicit `dotnet build` into the runtime path of the tests.

Before that change, the tests did **not** perform this extra explicit build inside the fixture.

## Why it was confusing

- Local runs stayed fast.
- macOS CI stayed fast.
- Windows CI did **not** even run the new `BackplaneIntegrationTests`, yet it was still slow.
- Connection lifecycle diagnostics were mostly 0-15 ms, so SignalR hot paths were innocent.

That made the issue look like a platform-specific runtime regression when it was actually a build orchestration regression.

## Final fix

We changed the fixtures to start a **prebuilt** `IntegrationTestServer.dll` instead of running a nested build during test execution.

To make that reliable in CI:

1. `IntegrationTestServer` remains in the integration test project's restore graph.
2. The integration test project explicitly builds `IntegrationTestServer` as part of its own build.
3. The fixture launches the prebuilt DLL directly.

This kept the runtime path fast while still guaranteeing that CI had the assets file and built server binary available.

## Guardrail

Do **not** add explicit `dotnet build` or equivalent build orchestration inside test fixtures, server bootstrappers, or runtime test setup unless there is a very strong reason and CI timings have been checked on all platforms.

If a helper executable or test server must exist before tests run:

1. keep it in the normal restore/build graph,
2. build it as part of the project build,
3. launch the prebuilt output from the fixture.

## Related commits

- `249c859` - removed the runtime nested build bottleneck and switched to launching the prebuilt server assembly
- `4f347bc` - explicitly built `IntegrationTestServer` from the integration test project
- `b12851d` - kept `IntegrationTestServer` in the restore graph so CI had `project.assets.json`
