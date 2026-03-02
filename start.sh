#!/bin/bash
# ── AI Desktop Companion Launcher ─────────────────────────────────────────────
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BACKEND_DIR="$SCRIPT_DIR/backend"
APP_PATH="$SCRIPT_DIR/build/DesktopCompanion.app"
LOG_FILE="/tmp/ai-backend.log"
PORT=5001
PYTHON="/Library/Frameworks/Python.framework/Versions/3.14/bin/python3"

# ── 1. Kill any stale backend ──────────────────────────────────────────────────
if lsof -ti:$PORT &>/dev/null; then
    echo "⚠️  Killing stale process on port $PORT..."
    lsof -ti:$PORT | xargs kill -9
    sleep 1
fi

# ── 2. Start backend ───────────────────────────────────────────────────────────
echo "🚀 Starting backend..."
cd "$BACKEND_DIR"
nohup "$PYTHON" main.py > "$LOG_FILE" 2>&1 &
BACKEND_PID=$!
echo "   PID: $BACKEND_PID  |  Logs: $LOG_FILE"

# ── 3. Wait until backend is ready ────────────────────────────────────────────
echo -n "   Waiting for backend"
for i in $(seq 1 15); do
    if curl -s --max-time 1 http://127.0.0.1:$PORT/character &>/dev/null; then
        echo " ✅"
        break
    fi
    echo -n "."
    sleep 1
done

# ── 4. Launch the app ─────────────────────────────────────────────────────────
if [ -d "$APP_PATH" ]; then
    echo "🎮 Launching DesktopCompanion..."
    open "$APP_PATH"
else
    echo "❌ Build not found at: $APP_PATH"
    echo "   Build from Unity first: File → Build And Run"
    exit 1
fi

echo ""
echo "✅ All done! Backend log: tail -f $LOG_FILE"
echo "   To stop backend: kill $BACKEND_PID  (or: lsof -ti:5001 | xargs kill)"

# Disown the backend so it keeps running after this script exits
disown $BACKEND_PID 2>/dev/null || true
