#!/usr/bin/env python3
"""
Desktop Companion Launcher
Starts both the AI backend and the Unity companion app.
The Unity app handles its own transparency via TransparentWindowMac.cs.
"""
import subprocess
import sys
import os
import time
import signal

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BACKEND_SCRIPT = os.path.join(SCRIPT_DIR, "main.py")

# Update this after building
BUILD_PATH = os.path.join(
    SCRIPT_DIR, "..", "build", "DesktopCompanion.app",
    "Contents", "MacOS", "DesktopCompanion"
)

def find_build():
    """Find the Unity build."""
    # Check common build locations
    candidates = [
        BUILD_PATH,
        os.path.join(SCRIPT_DIR, "..", "build", "DesktopCompanion.app"),
        os.path.expanduser("~/Desktop/DesktopCompanion.app"),
    ]
    for path in candidates:
        if os.path.exists(path):
            return path
    return None

def main():
    print("🎭 Desktop Companion Launcher")
    print("=" * 40)

    # Start the AI backend
    print("🧠 Starting AI backend...")
    backend = subprocess.Popen(
        [sys.executable, BACKEND_SCRIPT],
        cwd=SCRIPT_DIR,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(2)  # Give Flask time to start

    if backend.poll() is not None:
        print("❌ Backend failed to start!")
        stderr = backend.stderr.read().decode()
        if "Address already in use" in stderr:
            print("✅ Backend is already running — continuing.")
        else:
            print(stderr)
            return
    else:
        print("✅ Backend running on http://127.0.0.1:5001")

    # Find and launch the Unity build
    build_path = find_build()
    if build_path is None:
        print("❌ Unity build not found!")
        print("   Build the project first: Unity → File → Build Settings → Build")
        print(f"   Save to: {os.path.join(SCRIPT_DIR, '..', 'build')}")
        backend.terminate()
        return

    print(f"🎮 Launching companion: {build_path}")

    if build_path.endswith(".app"):
        unity = subprocess.Popen(["open", build_path])
    else:
        unity = subprocess.Popen([build_path])

    print("✅ Companion is running!")
    print("")
    print("Controls:")
    print("  • Type messages in the input field to chat")
    print("  • Click 'Switch' to change characters")
    print("  • Right-click drag to move the window")
    print("  • Press ESC to quit")
    print("")
    print("Press Ctrl+C to stop everything.")

    try:
        unity.wait()
    except KeyboardInterrupt:
        print("\n🛑 Shutting down...")
    finally:
        backend.terminate()
        if unity.poll() is None:
            unity.terminate()
        print("👋 Goodbye!")

if __name__ == "__main__":
    main()
