#!/usr/bin/env bash
# NativeAOT smoke test of the .NET client.
# Publishes src/tests/Cocoar.SignalARRR.AotSmoke with PublishAot, runs it against the
# IntegrationTestServer and fails when a step fails or ILC reports a warning from Cocoar.SignalARRR.*.
#
# Usage: ./scripts/run-aot-smoke.sh
# Needs the native toolchain NativeAOT requires (clang/zlib on Linux, Xcode tools on macOS).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEST_SERVER="$SCRIPT_DIR/test-server.sh"
PROJECT="$REPO_ROOT/src/tests/Cocoar.SignalARRR.AotSmoke/Cocoar.SignalARRR.AotSmoke.csproj"
OUT="$REPO_ROOT/.aot-smoke"

rm -rf "$OUT"
mkdir -p "$OUT"

echo "=== Publishing AotSmoke (NativeAOT) ==="
if ! dotnet publish "$PROJECT" -c Release -o "$OUT/bin" -nologo > "$OUT/publish.log" 2>&1; then
    tail -40 "$OUT/publish.log"
    echo "AOT smoke: publish failed" >&2
    exit 1
fi

# One line per warning; only those raised in our own assemblies fail the run.
grep -E "warning (IL|AOT)[0-9]+" "$OUT/publish.log" | sed -E 's/ \[[^]]*\]$//' | sort -u > "$OUT/ilc-warnings.txt" || true
OWN_WARNINGS=$(grep -c "Cocoar\.SignalARRR" "$OUT/ilc-warnings.txt" || true)
echo "ILC warnings: $(wc -l < "$OUT/ilc-warnings.txt" | tr -d ' ') total, $OWN_WARNINGS from Cocoar.SignalARRR.*"
if [ "$OWN_WARNINGS" != "0" ]; then
    grep "Cocoar\.SignalARRR" "$OUT/ilc-warnings.txt"
fi

echo "=== Acquiring IntegrationTestServer ==="
SERVER_URL=$("$TEST_SERVER" acquire)
trap '"$TEST_SERVER" release || true' EXIT
echo "Server URL: $SERVER_URL"

echo "=== Running AotSmoke ==="
RUN_EXIT=0
"$OUT/bin/Cocoar.SignalARRR.AotSmoke" "$SERVER_URL" || RUN_EXIT=$?

if [ "$RUN_EXIT" != "0" ] || [ "$OWN_WARNINGS" != "0" ]; then
    echo "AOT smoke: FAILED" >&2
    exit 1
fi
echo "AOT smoke: passed"
