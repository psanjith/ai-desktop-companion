#!/bin/bash
set -euo pipefail

APP_PATH="$1"
ZIP_PATH="$2"
APP_NAME="$3"

LOG_DIR="$HOME/Library/Logs/DesktopCompanion"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/updater.log"

exec >> "$LOG_FILE" 2>&1

echo "==== $(date) : updater started ===="
echo "APP_PATH=$APP_PATH"
echo "ZIP_PATH=$ZIP_PATH"

if [[ ! -f "$ZIP_PATH" ]]; then
  echo "zip not found, aborting"
  exit 1
fi

# Wait for app process to fully exit (up to ~90s)
for _ in {1..90}; do
  if ! pgrep -f "$APP_NAME.app/Contents/MacOS" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

TMP_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP_DIR" || true
}
trap cleanup EXIT

unzip -oq "$ZIP_PATH" -d "$TMP_DIR"
NEW_APP="$(find "$TMP_DIR" -maxdepth 3 -type d -name "*.app" | head -n 1)"

if [[ -z "$NEW_APP" ]]; then
  echo "no .app found in zip, aborting"
  exit 1
fi

echo "new app found at $NEW_APP"

# Replace app bundle in place
rm -rf "$APP_PATH"
cp -R "$NEW_APP" "$APP_PATH"

# Best-effort clear quarantine
xattr -dr com.apple.quarantine "$APP_PATH" || true

echo "launching updated app"
open "$APP_PATH"
echo "==== $(date) : updater finished ===="
