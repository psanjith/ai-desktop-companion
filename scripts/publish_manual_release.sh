#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ./scripts/publish_manual_release.sh 1.0.1 "/path/to/DesktopCompanion.app"

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <version> <path-to-DesktopCompanion.app>"
  exit 1
fi

VERSION="$1"
APP_PATH="$2"

if [[ ! -d "$APP_PATH" ]]; then
  echo "App bundle not found: $APP_PATH"
  exit 1
fi

if ! command -v gh >/dev/null 2>&1; then
  echo "GitHub CLI (gh) is required. Install and run: gh auth login"
  exit 1
fi

REPO_SLUG="$(git remote get-url origin | sed -E 's#(git@github.com:|https://github.com/)##; s#\.git$##')"
if [[ -z "$REPO_SLUG" ]]; then
  echo "Could not resolve repo slug from git remote."
  exit 1
fi

TAG="v${VERSION}"
BUILD_DIR="build/manual-release"
ZIP_PATH="$BUILD_DIR/DesktopCompanion-mac.zip"
MANIFEST_PATH="$BUILD_DIR/update.json"

mkdir -p "$BUILD_DIR"
rm -f "$ZIP_PATH" "$MANIFEST_PATH"

echo "Packaging app..."
# macOS-native zip preserving app bundle metadata
/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$APP_PATH" "$ZIP_PATH"

SHA256="$(shasum -a 256 "$ZIP_PATH" | awk '{print $1}')"
PACKAGE_URL="https://github.com/${REPO_SLUG}/releases/download/${TAG}/DesktopCompanion-mac.zip"

cat > "$MANIFEST_PATH" <<EOF
{
  "latestVersion": "${VERSION}",
  "url": "${PACKAGE_URL}",
  "sha256": "${SHA256}",
  "mandatory": false,
  "notes": "Manual release ${TAG}"
}
EOF

echo "Creating GitHub release ${TAG}..."
if gh release view "$TAG" >/dev/null 2>&1; then
  gh release upload "$TAG" "$ZIP_PATH" "$MANIFEST_PATH" --clobber
else
  gh release create "$TAG" "$ZIP_PATH" "$MANIFEST_PATH" \
    --title "DesktopCompanion ${VERSION}" \
    --notes "Manual release ${TAG}" \
    --latest
fi

echo "Done."
echo "Manifest URL: https://github.com/${REPO_SLUG}/releases/latest/download/update.json"
