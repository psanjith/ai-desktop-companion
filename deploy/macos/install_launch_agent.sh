#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PLIST_TARGET="$HOME/Library/LaunchAgents/com.aidesktopcompanion.agent.plist"
RUNNER_PATH="$SCRIPT_DIR/run_companion.sh"
APP_PATH="${1:-/Applications/DesktopCompanion.app}"

mkdir -p "$HOME/Library/LaunchAgents"

cat > "$PLIST_TARGET" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>com.aidesktopcompanion.agent</string>

  <key>ProgramArguments</key>
  <array>
    <string>/bin/bash</string>
    <string>$RUNNER_PATH</string>
    <string>$APP_PATH</string>
  </array>

  <key>RunAtLoad</key>
  <true/>

  <key>KeepAlive</key>
  <true/>

  <key>StandardOutPath</key>
  <string>/tmp/desktopcompanion-agent.out.log</string>
  <key>StandardErrorPath</key>
  <string>/tmp/desktopcompanion-agent.err.log</string>
</dict>
</plist>
EOF

launchctl unload "$PLIST_TARGET" >/dev/null 2>&1 || true
launchctl load "$PLIST_TARGET"

echo "✅ Installed launch agent"
echo "   Label: com.aidesktopcompanion.agent"
echo "   App:   $APP_PATH"
echo "   Plist: $PLIST_TARGET"
