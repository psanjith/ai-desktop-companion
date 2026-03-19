#!/bin/bash
set -euo pipefail

APP_PATH="$1"
PACKAGE_PATH="$2"
APP_NAME="$3"

LOG_DIR="$HOME/Library/Logs/DesktopCompanion"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/updater.log"

exec >> "$LOG_FILE" 2>&1

echo "==== $(date) : updater started ===="
echo "APP_PATH=$APP_PATH"
echo "PACKAGE_PATH=$PACKAGE_PATH"

if [[ ! -f "$PACKAGE_PATH" ]]; then
  echo "package not found, aborting"
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

if [[ "$PACKAGE_PATH" == *.tar.gz ]]; then
  tar -xzf "$PACKAGE_PATH" -C "$TMP_DIR"
elif [[ "$PACKAGE_PATH" == *.zip ]]; then
  unzip -oq "$PACKAGE_PATH" -d "$TMP_DIR"
else
  echo "unsupported package format: $PACKAGE_PATH"
  exit 1
fi

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
