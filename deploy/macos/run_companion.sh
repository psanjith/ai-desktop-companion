#!/bin/bash
set -euo pipefail

APP_PATH="${1:-/Applications/DesktopCompanion.app}"
APP_NAME="$(basename "$APP_PATH" .app)"

if [ ! -d "$APP_PATH" ]; then
  echo "❌ App not found: $APP_PATH"
  exit 1
fi

if pgrep -ix "$APP_NAME" >/dev/null 2>&1; then
  exit 0
fi

open "$APP_PATH"
