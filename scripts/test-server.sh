#!/usr/bin/env bash
# Unified IntegrationTestServer lifecycle manager.
# Tracks active clients via reference counting. Server starts on first acquire, stops on last release.
#
# Usage:
#   ./scripts/test-server.sh acquire   → starts server if needed, prints URL, increments client count
#   ./scripts/test-server.sh release   → decrements client count, stops server when count reaches 0
#   ./scripts/test-server.sh status    → prints current state (running/stopped, URL, client count)
#   ./scripts/test-server.sh kill      → force-stops server regardless of client count

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
STATE_DIR="$REPO_ROOT/.test-server"
PID_FILE="$STATE_DIR/pid"
URL_FILE="$STATE_DIR/url"
COUNT_FILE="$STATE_DIR/count"
LOG_FILE="$STATE_DIR/server.log"
SERVER_PROJECT="$REPO_ROOT/src/tests/IntegrationTestServer"
LOCK_FILE="$STATE_DIR/lock"
MAX_WAIT=60

mkdir -p "$STATE_DIR"

# --- Locking (file-based, cross-platform) ---

acquire_lock() {
    local tries=0
    while ! (set -o noclobber; echo $$ > "$LOCK_FILE") 2>/dev/null; do
        tries=$((tries + 1))
        if [ $tries -ge 100 ]; then
            echo "ERROR: Could not acquire lock after 10 seconds" >&2
            exit 1
        fi
        sleep 0.1
    done
}

release_lock() {
    rm -f "$LOCK_FILE"
}

# --- Helpers ---

read_count() {
    if [ -f "$COUNT_FILE" ]; then
        cat "$COUNT_FILE"
    else
        echo "0"
    fi
}

write_count() {
    echo "$1" > "$COUNT_FILE"
}

is_server_running() {
    if [ -f "$PID_FILE" ]; then
        local pid
        pid=$(cat "$PID_FILE")
        if kill -0 "$pid" 2>/dev/null; then
            return 0
        fi
    fi
    return 1
}

start_server() {
    local tfm="${DOTNET_TARGET_FRAMEWORK:-net10.0}"
    local framework_arg="--framework $tfm"
    echo "Target framework: $tfm" >&2

    echo "Building IntegrationTestServer..." >&2
    dotnet build "$SERVER_PROJECT" -c Release --verbosity quiet $framework_arg >&2

    local server_url_file
    server_url_file="$STATE_DIR/url_discovery"

    echo "Starting IntegrationTestServer..." >&2
    SERVER_URL_FILE="$server_url_file" \
        dotnet run --project "$SERVER_PROJECT" -c Release --no-build $framework_arg \
        > "$LOG_FILE" 2>&1 &
    local server_pid=$!
    echo "$server_pid" > "$PID_FILE"

    # Wait for server to write its URL
    local elapsed=0
    while [ $elapsed -lt $MAX_WAIT ]; do
        if [ -f "$server_url_file" ]; then
            local url
            url=$(cat "$server_url_file")
            if [ -n "$url" ]; then
                echo "$url" > "$URL_FILE"
                rm -f "$server_url_file"
                echo "IntegrationTestServer started (PID $server_pid): $url" >&2
                return 0
            fi
        fi
        # Check if process died
        if ! kill -0 "$server_pid" 2>/dev/null; then
            echo "ERROR: IntegrationTestServer exited unexpectedly. Log:" >&2
            cat "$LOG_FILE" >&2
            cleanup_state
            return 1
        fi
        sleep 0.5
        elapsed=$((elapsed + 1))
    done

    echo "ERROR: IntegrationTestServer did not start within ${MAX_WAIT}s" >&2
    kill "$server_pid" 2>/dev/null || true
    cleanup_state
    return 1
}

stop_server() {
    if [ -f "$PID_FILE" ]; then
        local pid
        pid=$(cat "$PID_FILE")
        if kill -0 "$pid" 2>/dev/null; then
            echo "Stopping IntegrationTestServer (PID $pid)..." >&2
            kill "$pid" 2>/dev/null || true
            # Wait briefly for graceful shutdown
            local tries=0
            while kill -0 "$pid" 2>/dev/null && [ $tries -lt 10 ]; do
                sleep 0.5
                tries=$((tries + 1))
            done
            # Force kill if still alive
            if kill -0 "$pid" 2>/dev/null; then
                kill -9 "$pid" 2>/dev/null || true
            fi
        fi
    fi
    cleanup_state
}

cleanup_state() {
    rm -f "$PID_FILE" "$URL_FILE" "$COUNT_FILE" "$LOCK_FILE" "$STATE_DIR/url_discovery"
}

# --- Commands ---

cmd_acquire() {
    acquire_lock
    trap release_lock EXIT

    local count
    count=$(read_count)

    if [ "$count" -eq 0 ] || ! is_server_running; then
        # First client or server crashed — (re)start
        if is_server_running; then
            stop_server
        fi
        if ! start_server; then
            release_lock
            exit 1
        fi
        count=0
    fi

    count=$((count + 1))
    write_count "$count"

    local url
    url=$(cat "$URL_FILE")
    echo "Client count: $count" >&2
    # Print URL to stdout (this is what callers capture)
    echo "$url"
}

cmd_release() {
    acquire_lock
    trap release_lock EXIT

    local count
    count=$(read_count)

    if [ "$count" -le 0 ]; then
        echo "No active clients to release" >&2
        return 0
    fi

    count=$((count - 1))
    write_count "$count"

    echo "Client count: $count" >&2

    if [ "$count" -eq 0 ]; then
        stop_server
        echo "IntegrationTestServer stopped (no more clients)" >&2
    fi
}

cmd_status() {
    if is_server_running; then
        local pid url count
        pid=$(cat "$PID_FILE")
        url=$(cat "$URL_FILE" 2>/dev/null || echo "unknown")
        count=$(read_count)
        echo "Running (PID $pid)"
        echo "URL: $url"
        echo "Active clients: $count"
    else
        echo "Stopped"
    fi
}

cmd_kill() {
    stop_server
    echo "IntegrationTestServer killed" >&2
}

# --- Main ---

case "${1:-}" in
    acquire) cmd_acquire ;;
    release) cmd_release ;;
    status)  cmd_status ;;
    kill)    cmd_kill ;;
    *)
        echo "Usage: $0 {acquire|release|status|kill}" >&2
        exit 1
        ;;
esac
