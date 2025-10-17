# Integration Tests

This project contains integration tests that spin up a real test server and validate end-to-end functionality.

## Test Duration

These tests take approximately **86 seconds** to run because they:
- Start a real ASP.NET Core test server
- Establish SignalR connections
- Test real network communication
- Validate full client-server interactions

## Running Tests

Run all integration tests:
```bash
dotnet test Cocoar.SignalARRR.IntegrationTests.csproj
```

## Test Coverage

- **ClientToServerTests** - Tests client invoking server methods (9 tests)
- **ErrorHandlingTests** - Tests error handling scenarios (2 tests)
- **SimpleHARRRConnectionTests** - Tests basic HARRR connection functionality (2 tests)
- **SimpleHubConnectionTests** - Tests basic hub connection functionality (2 tests)
- **StreamingTests** - Tests streaming capabilities (2 tests)
- **TypedHARRRConnectionTests** - Tests typed HARRR connections (2 tests)

**Total: 19 integration tests**

## CI/CD

These tests should be run on every push to ensure nothing breaks, but they are separated from unit tests to:
1. Make it clear why the test suite takes longer
2. Allow parallel test execution in CI pipelines
3. Enable selective test execution during development
