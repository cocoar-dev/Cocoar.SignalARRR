#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "==> Building IntegrationTestServer..."
dotnet build "$REPO_ROOT/src/tests/IntegrationTestServer" -c Release

# Create a temp file for the server to write its URL to
URL_FILE=$(mktemp)
trap 'rm -f "$URL_FILE"; [ -n "${SERVER_PID:-}" ] && kill "$SERVER_PID" 2>/dev/null || true' EXIT

echo "==> Starting IntegrationTestServer..."
SERVER_URL_FILE="$URL_FILE" dotnet run --project "$REPO_ROOT/src/tests/IntegrationTestServer" -c Release --no-build &
SERVER_PID=$!

# Wait for the server URL file to be populated (up to 30 seconds)
echo "==> Waiting for server to start..."
for i in $(seq 1 30); do
    if [ -s "$URL_FILE" ]; then
        break
    fi
    sleep 1
done

if [ ! -s "$URL_FILE" ]; then
    echo "ERROR: Server did not start within 30 seconds"
    exit 1
fi

SERVER_URL=$(cat "$URL_FILE")
echo "==> Server started at $SERVER_URL"

echo "==> Running Swift integration tests..."
cd "$REPO_ROOT"
SIGNALARRR_TEST_SERVER_URL="$SERVER_URL" swift test --filter Integration

echo "==> Integration tests passed!"
