# Manual release flow (recommended while CI license is unstable)

This avoids Unity license activation in GitHub Actions.

## One-time setup

1. Install GitHub CLI (`gh`) and login:
   - `gh auth login`
2. Build `DesktopCompanion.app` locally from Unity (macOS build target).

## Publish a new update

Run:

- `./scripts/publish_manual_release.sh <version> "/absolute/path/to/DesktopCompanion.app"`

Example:

- `./scripts/publish_manual_release.sh 1.0.1 "/Users/you/Downloads/DesktopCompanion.app"`

What this script does:

1. Creates `DesktopCompanion-mac.zip`
2. Computes SHA256
3. Creates `update.json`
4. Creates (or updates) GitHub Release tag `v<version>`
5. Uploads both assets

## Updater URL

The app checks:

- `https://github.com/psanjith/ai-desktop-companion/releases/latest/download/update.json`

As soon as a newer version is published, installed apps can update.

## Notes

- Keep version numbers increasing (`1.0.0`, `1.0.1`, ...)
- If repo is private, users without access cannot download updates.
