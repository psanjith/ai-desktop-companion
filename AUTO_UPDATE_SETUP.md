# DesktopCompanion Auto-Update Setup

This repo now includes starter files for in-app macOS auto-updates:

- Unity updater runtime: [unity-client/DesktopCompanion/Assets/UpdateManager.cs](unity-client/DesktopCompanion/Assets/UpdateManager.cs)
- Installer script: [unity-client/DesktopCompanion/Assets/StreamingAssets/updater/install_update.sh](unity-client/DesktopCompanion/Assets/StreamingAssets/updater/install_update.sh)
- Updater config: [unity-client/DesktopCompanion/Assets/StreamingAssets/updater/config.json](unity-client/DesktopCompanion/Assets/StreamingAssets/updater/config.json)
- GitHub Actions release pipeline: [.github/workflows/release-mac.yml](.github/workflows/release-mac.yml)

## How it works

1. App starts and `UpdateManager` checks `update.json`.
2. If `latestVersion` > `Application.version`, it downloads the zip.
3. It validates SHA256.
4. It launches `install_update.sh` and quits.
5. Script replaces the `.app` and relaunches it.

## What you need to configure

## 1) Unity versioning

Before each release, update Unity Player `bundleVersion` in:

- [unity-client/DesktopCompanion/ProjectSettings/ProjectSettings.asset](unity-client/DesktopCompanion/ProjectSettings/ProjectSettings.asset)

Current value should be semver-like (`1.0.0`, `1.0.1`, etc).

## 2) GitHub Actions Unity secrets

In GitHub repo settings → Secrets and variables → Actions, add:

- `UNITY_LICENSE`
- `UNITY_EMAIL`
- `UNITY_PASSWORD`

If you use a different auth model, adjust workflow accordingly.

## 3) Release visibility

`config.json` points to:

`https://github.com/psanjith/ai-desktop-companion/releases/latest/download/update.json`

Releases must be accessible to your users.

## 4) First install requirement

Users still install the app manually once.
After that, updates are delivered in-app.

## 5) Optional but recommended

- Code-sign and notarize the app.
- Add rollback behavior if install fails.
- Add update UI prompt instead of fully silent updates.

## Quick validation checklist

1. Bump `bundleVersion`.
2. Push to `main`.
3. Wait for workflow to publish release assets.
4. Launch an older app build.
5. Confirm it downloads and relaunches into the newer build.
6. Check updater logs at:
   - `~/Library/Logs/DesktopCompanion/updater.log`
