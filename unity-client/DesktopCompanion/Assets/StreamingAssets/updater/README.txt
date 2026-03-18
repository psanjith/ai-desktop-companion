DesktopCompanion updater assets

Files:
- config.json        : runtime updater settings
- install_update.sh  : macOS bundle replacement script

Notes:
- install_update.sh is launched by UpdateManager and runs outside Unity.
- config.json manifestUrl should point to your published update.json.
