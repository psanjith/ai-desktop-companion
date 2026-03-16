#!/bin/bash
set -euo pipefail

PLIST_TARGET="$HOME/Library/LaunchAgents/com.aidesktopcompanion.agent.plist"

launchctl unload "$PLIST_TARGET" >/dev/null 2>&1 || true
rm -f "$PLIST_TARGET"

echo "✅ Uninstalled launch agent: com.aidesktopcompanion.agent"
