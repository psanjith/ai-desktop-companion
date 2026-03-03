#!/bin/bash
# ── AI Desktop Companion Launcher ─────────────────────────────────────────────
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BACKEND_DIR="$SCRIPT_DIR/backend"
APP_PATH="$SCRIPT_DIR/build/DesktopCompanion.app"
LOG_FILE="/tmp/ai-backend.log"
PORT=5001

# ── 0. Auto-detect Python ─────────────────────────────────────────────────────
find_python() {
    for candidate in \
        "$(command -v python3 2>/dev/null)" \
        "/opt/homebrew/bin/python3" \
        "/usr/local/bin/python3" \
        "/usr/bin/python3" \
        "/Library/Frameworks/Python.framework/Versions/3.14/bin/python3" \
        "/Library/Frameworks/Python.framework/Versions/3.13/bin/python3" \
        "/Library/Frameworks/Python.framework/Versions/3.12/bin/python3" \
        "/Library/Frameworks/Python.framework/Versions/3.11/bin/python3"; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done
    return 1
}

PYTHON=$(find_python) || { echo "❌ Python 3 not found. Install from python.org or via Homebrew."; exit 1; }
echo "🐍 Using Python: $PYTHON"

# ── 1. Check Ollama is running ────────────────────────────────────────────────
if ! curl -s --max-time 2 http://localhost:11434/api/tags &>/dev/null; then
    echo "⚠️  Ollama is not running. Attempting to start it..."
    if command -v ollama &>/dev/null; then
        ollama serve &>/tmp/ollama.log &
        echo -n "   Waiting for Ollama"
        for i in $(seq 1 10); do
            if curl -s --max-time 1 http://localhost:11434/api/tags &>/dev/null; then
                echo " ✅"
                break
            fi
            echo -n "."
            sleep 1
        done
        if ! curl -s --max-time 1 http://localhost:11434/api/tags &>/dev/null; then
            echo ""
            echo "❌ Ollama did not start. Run 'ollama serve' manually, then try again."
            exit 1
        fi
    else
        echo "❌ Ollama not installed. Download from https://ollama.ai and run 'ollama pull llama3.2:3b'."
        exit 1
    fi
else
    echo "✅ Ollama is running."
fi

# ── 2. Kill any stale backend ──────────────────────────────────────────────────
if lsof -ti:$PORT &>/dev/null; then
    echo "⚠️  Killing stale process on port $PORT..."
    lsof -ti:$PORT | xargs kill -9
    sleep 1
fi

# ── 3. Start backend ───────────────────────────────────────────────────────────
echo "🚀 Starting backend..."
cd "$BACKEND_DIR"
nohup "$PYTHON" main.py > "$LOG_FILE" 2>&1 &
BACKEND_PID=$!
echo "   PID: $BACKEND_PID  |  Logs: $LOG_FILE"

# ── 4. Wait until backend is ready ────────────────────────────────────────────
echo -n "   Waiting for backend"
HEALTHY=false
for i in $(seq 1 15); do
    if curl -s --max-time 1 http://127.0.0.1:$PORT/character &>/dev/null; then
        echo " ✅"
        HEALTHY=true
        break
    fi
    echo -n "."
    sleep 1
done
if [ "$HEALTHY" != "true" ]; then
    echo ""
    echo "❌ Backend did not start within 15s. Check logs: tail -f $LOG_FILE"
    exit 1
fi

# ── 5. Launch the app ─────────────────────────────────────────────────────────
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
