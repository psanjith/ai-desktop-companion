# Desktop Companion: installable background app mode (macOS)

This setup makes the companion behave like wallpaper-style software:
- user installs one app
- it starts automatically at login
- it stays running in the background

## 1) Host backend once
Deploy the Flask backend and keep API keys server-side.

## 2) Point Unity app to hosted backend
In Unity, set `CompanionController.backendBaseUrl` to your HTTPS backend URL.

Example:
- `https://your-backend.example.com`

## 3) Build distributable app
Build `DesktopCompanion.app` and place it at:
- `/Applications/DesktopCompanion.app`

## 4) Enable auto-start background mode
From this repo, run:

- `bash deploy/macos/install_launch_agent.sh`

Or if app is in custom location:

- `bash deploy/macos/install_launch_agent.sh "/path/to/DesktopCompanion.app"`

This installs a user LaunchAgent:
- `~/Library/LaunchAgents/com.aidesktopcompanion.agent.plist`

## 5) Disable later (optional)

- `bash deploy/macos/uninstall_launch_agent.sh`

## Notes
- `KeepAlive` is enabled, so if the app exits it is relaunched.
- Logs are written to:
  - `/tmp/desktopcompanion-agent.out.log`
  - `/tmp/desktopcompanion-agent.err.log`
- For public release, sign and notarize your `.app`.
