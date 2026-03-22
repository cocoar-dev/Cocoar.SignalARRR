#!/usr/bin/env bash
# Run integration tests for all available client platforms against the shared IntegrationTestServer.
# Usage:
#   ./scripts/run-integration-tests.sh              # run all available clients
#   ./scripts/run-integration-tests.sh dotnet        # .NET only
#   ./scripts/run-integration-tests.sh swift         # Swift only
#   ./scripts/run-integration-tests.sh typescript    # TypeScript only

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_SERVER="$SCRIPT_DIR/test-server.sh"

FILTER="${1:-all}"
FAILED=0

# --- Acquire server ---

echo "=== Acquiring IntegrationTestServer ==="
SERVER_URL=$("$TEST_SERVER" acquire)
export SIGNALARRR_TEST_SERVER_URL="$SERVER_URL"
echo "Server URL: $SERVER_URL"

cleanup() {
    echo ""
    echo "=== Releasing IntegrationTestServer ==="
    "$TEST_SERVER" release || true
}
trap cleanup EXIT

# --- .NET Tests ---

if [ "$FILTER" = "all" ] || [ "$FILTER" = "dotnet" ]; then
    echo ""
    echo "=== Running .NET Integration Tests ==="
    if dotnet test "$REPO_ROOT/src/tests/Cocoar.SignalARRR.IntegrationTests" \
        -c Release --verbosity quiet --filter "Type!=Performance"; then
        echo ".NET tests: PASSED"
    else
        echo ".NET tests: FAILED"
        FAILED=1
    fi
fi

# --- Swift Tests ---

if [ "$FILTER" = "all" ] || [ "$FILTER" = "swift" ]; then
    if command -v swift &>/dev/null; then
        echo ""
        echo "=== Running Swift Integration Tests ==="
        if (cd "$REPO_ROOT" && swift test --filter Integration); then
            echo "Swift tests: PASSED"
        else
            echo "Swift tests: FAILED"
            FAILED=1
        fi
    else
        echo ""
        echo "=== Skipping Swift Tests (swift not available) ==="
    fi
fi

# --- TypeScript Tests ---

if [ "$FILTER" = "all" ] || [ "$FILTER" = "typescript" ]; then
    TS_DIR="$REPO_ROOT/src/Cocoar.SignalARRR.Typescript"
    if [ -f "$TS_DIR/package.json" ] && [ -d "$TS_DIR/tests" ]; then
        echo ""
        echo "=== Running TypeScript Integration Tests ==="
        if (cd "$TS_DIR" && npm test); then
            echo "TypeScript tests: PASSED"
        else
            echo "TypeScript tests: FAILED"
            FAILED=1
        fi
    else
        echo ""
        echo "=== Skipping TypeScript Tests (no test directory) ==="
    fi
fi

# --- Summary ---

echo ""
if [ $FAILED -eq 0 ]; then
    echo "=== All tests PASSED ==="
else
    echo "=== Some tests FAILED ==="
    exit 1
fi
