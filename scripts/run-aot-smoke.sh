#!/usr/bin/env bash
# NativeAOT smoke test of the .NET client and server.
# Publishes the client probe (src/tests/Cocoar.SignalARRR.AotSmoke) and the smoke server
# (src/tests/Cocoar.SignalARRR.AotSmokeServer) with PublishAot, then runs the native probe
#   1. against the native smoke server, with the steps for the generated server dispatch, and
#   2. against the IntegrationTestServer (JIT), for the client paths alone.
# Fails when a step fails or ILC reports a warning from Cocoar.SignalARRR.* in either publish.
#
# Usage: ./scripts/run-aot-smoke.sh
# Needs the native toolchain NativeAOT requires (clang/zlib on Linux, Xcode tools on macOS).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_SERVER="$SCRIPT_DIR/test-server.sh"
CLIENT_PROJECT="$REPO_ROOT/src/tests/Cocoar.SignalARRR.AotSmoke/Cocoar.SignalARRR.AotSmoke.csproj"
SERVER_PROJECT="$REPO_ROOT/src/tests/Cocoar.SignalARRR.AotSmokeServer/Cocoar.SignalARRR.AotSmokeServer.csproj"
OUT="$REPO_ROOT/.aot-smoke"

rm -rf "$OUT"
mkdir -p "$OUT"

OWN_WARNINGS=0

# Publishes one project with NativeAOT and counts the ILC warnings raised in our own assemblies.
publish() {
    local name="$1" project="$2"
    echo "=== Publishing $name (NativeAOT) ==="
    if ! dotnet publish "$project" -c Release -o "$OUT/$name" -nologo > "$OUT/$name-publish.log" 2>&1; then
        tail -40 "$OUT/$name-publish.log"
        echo "AOT smoke: publish of $name failed" >&2
        exit 1
    fi

    grep -E "warning (IL|AOT)[0-9]+" "$OUT/$name-publish.log" | sed -E 's/ \[[^]]*\]$//' | sort -u > "$OUT/$name-ilc-warnings.txt" || true
    local own
    own=$(grep -c "Cocoar\.SignalARRR" "$OUT/$name-ilc-warnings.txt" || true)
    echo "ILC warnings ($name): $(wc -l < "$OUT/$name-ilc-warnings.txt" | tr -d ' ') total, $own from Cocoar.SignalARRR.*"
    if [ "$own" != "0" ]; then
        grep "Cocoar\.SignalARRR" "$OUT/$name-ilc-warnings.txt"
        OWN_WARNINGS=$((OWN_WARNINGS + own))
    fi
}

publish client "$CLIENT_PROJECT"
publish server "$SERVER_PROJECT"

SMOKE_SERVER_PID=""
cleanup() {
    if [ -n "$SMOKE_SERVER_PID" ]; then kill "$SMOKE_SERVER_PID" 2>/dev/null || true; fi
    if [ "${TEST_SERVER_ACQUIRED:-0}" = "1" ]; then "$TEST_SERVER" release || true; fi
}
trap cleanup EXIT

echo "=== Starting the NativeAOT smoke server ==="
"$OUT/server/Cocoar.SignalARRR.AotSmokeServer" > "$OUT/server.log" 2>&1 &
SMOKE_SERVER_PID=$!
SMOKE_URL=""
for _ in $(seq 1 60); do
    SMOKE_URL=$(grep -o 'SERVER_URL=.*' "$OUT/server.log" 2>/dev/null | head -1 | cut -d= -f2 | tr -d '\r' || true)
    [ -n "$SMOKE_URL" ] && break
    if ! kill -0 "$SMOKE_SERVER_PID" 2>/dev/null; then break; fi
    sleep 0.5
done
if [ -z "$SMOKE_URL" ]; then
    cat "$OUT/server.log"
    echo "AOT smoke: the smoke server did not start" >&2
    exit 1
fi
echo "Smoke server URL: $SMOKE_URL"

RUN_EXIT=0
echo "=== Running AotSmoke against the NativeAOT smoke server ==="
"$OUT/client/Cocoar.SignalARRR.AotSmoke" "$SMOKE_URL" --smoke-server || RUN_EXIT=1

echo "=== Acquiring IntegrationTestServer ==="
SERVER_URL=$("$TEST_SERVER" acquire)
TEST_SERVER_ACQUIRED=1
echo "Server URL: $SERVER_URL"

echo "=== Running AotSmoke against the IntegrationTestServer ==="
"$OUT/client/Cocoar.SignalARRR.AotSmoke" "$SERVER_URL" || RUN_EXIT=1

if [ "$RUN_EXIT" != "0" ] || [ "$OWN_WARNINGS" != "0" ]; then
    if [ -s "$OUT/server.log" ]; then
        echo "--- smoke server log ---"
        tail -40 "$OUT/server.log"
    fi
    echo "AOT smoke: FAILED" >&2
    exit 1
fi
echo "AOT smoke: passed"
