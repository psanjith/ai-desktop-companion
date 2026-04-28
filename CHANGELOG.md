# Changelog

All notable changes to Luren — AI Desktop Companion are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - April 27, 2026

### ✨ Initial Release

**Luren is now available for download on macOS, Windows, and Linux!**

#### Added

- **Multi-Platform Support**
  - Native builds for macOS (Intel & Apple Silicon)
  - Windows 10/11 support
  - Linux support (x86_64)

- **Expressive AI Companion**
  - Real-time speech recognition (Whisper)
  - Natural language processing with emotion detection
  - Adaptive personality system (Luna & Ren characters)
  - Context-aware responses

- **Minimal, Intentional Movements**
  - Gesture validation system ensures all movements match emotional context
  - Emotion-based emote mapping (joy, sorrow, anger, etc.)
  - Context detection (question, greeting, farewell, agreement, disagreement, apology)
  - No unnecessary or distracting animations

- **Lightning-Fast Performance**
  - Optimized startup time (~1-1.5 seconds)
  - Rapid first response (~1.5-2 seconds)
  - Efficient memory usage
  - Works great on Render and other cloud platforms

- **Auto-Update System**
  - Desktop app automatically checks for new versions
  - One-click update mechanism
  - Transparent changelog viewing

- **Multi-LLM Support**
  - Primary: Groq (llama-3.1-8b-instant) — fastest option
  - Fallback: OpenAI (gpt-4-turbo, gpt-3.5-turbo)
  - Local: Ollama support for complete privacy

- **Privacy First**
  - Local configuration (no cloud storage of settings)
  - Optional local LLM via Ollama
  - All data stays on your device

- **Easy Installation**
  - Download from landing page (no command line needed)
  - One-click installation
  - Auto-configured on first launch

#### Features

- **Idle Check-ins**: Companion checks in when you haven't interacted for a while
- **Personality Persistence**: Character traits remain consistent across conversations
- **Gesture System**: Emotion-driven animations that respect conversational context
- **Health Monitoring**: App stays responsive and detects connection issues
- **Streaming Responses**: Real-time response streaming for better UX

#### Technical

- **Backend**: Flask-based REST API
- **Frontend**: Unity 3D desktop client
- **Speech Recognition**: OpenAI Whisper (CPU optimized)
- **LLM Integration**: Groq API with provider fallbacks
- **Deployment**: GitHub Pages landing page with auto-update manifests

#### Known Limitations

- Initial Whisper model download (~1GB) on first transcription
- macOS users may see "unverified developer" warning (code signing coming in v1.1.0)
- Some animations may feel stiff on lower-end hardware

#### Installation

**macOS / Windows / Linux:**
1. Visit: https://psanjith.github.io/ai-desktop-companion/
2. Download for your platform
3. Extract and run
4. Configure API key on first launch

---

## Future Releases

### Planned for v1.1.0
- Code signing for macOS (eliminate unverified developer warning)
- Additional personality characters
- Customizable gesture sets
- Background noise suppression
- Performance improvements for older hardware

### Planned for v1.2.0
- Web dashboard for configuration management
- Conversation history viewing and export
- Custom personality creation
- Gesture recording/customization
- Advanced privacy controls (local-only mode)

### Planned for v2.0.0
- Multi-monitor support
- Voice cloning (custom voice generation)
- Integration with system apps (calendar, email, todo)
- Plugin system for community extensions
- Advanced gesture library with machine learning

---

## Support

- **Issues**: https://github.com/psanjith/ai-desktop-companion/issues
- **Discussions**: https://github.com/psanjith/ai-desktop-companion/discussions
- **Releases**: https://github.com/psanjith/ai-desktop-companion/releases

---

## Version History

| Version | Date | Status |
|---------|------|--------|
| 1.0.0 | April 27, 2026 | ✅ Current Release |

---

**Thank you for using Luren! Feedback welcome.** 🎉
