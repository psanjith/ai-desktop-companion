# Async TTS Implementation

## Overview

The backend now supports **asynchronous TTS generation**, which dramatically improves perceived response latency for voice interactions. Text responses are returned immediately while audio is generated in the background.

### Before (Blocking)
```
Request
  ↓
LLM generates text (8-15s)
  ↓
TTS generates audio (2-5s)  ← BLOCKING
  ↓
Return both
  ↓
Client plays audio & shows text (total: 10-20s before anything visible)
```

### After (Async)
```
Request
  ↓
LLM generates text (8-15s)
  ↓
Return text + TTS token immediately (8-15s total)  ← Client shows text NOW
  ↓
TTS audio generated in background (2-5s)
  ↓
Client downloads audio via token
  ↓
Client plays audio while text already visible
```

**Result**: Text appears in ~8-15s instead of 20-30s. Audio follows when ready.

## Usage

### Client Implementation

1. **Send chat request with TTS flag**:
```python
response = requests.post(
    "http://127.0.0.1:5001/chat",
    json={
        "message": "hey whats up",
        "character": "female_default",
        "tts": True  # Request async TTS generation
    }
)
```

2. **Response structure**:
```json
{
  "reply": "Not much, just hanging out! What's new with you?",
  "emotes": ["leans forward", "raises hand"],
  "emotion": "neutral",
  "tts_token": "2cac4582824f499689a4afc11c0a777d",
  "audio_url": "https://127.0.0.1:5001/speak/audio/2cac4582824f499689a4afc11c0a777d.mp3"
}
```

3. **Show text immediately** (don't wait for audio):
```python
# Display reply, emotes, and emotion right away
display_message(response["reply"])
play_emotes(response["emotes"])
```

4. **Download and play audio when ready**:
```python
# Option A: Poll for readiness
audio_ready = False
for attempt in range(30):  # Try for up to 30 seconds
    status = requests.get(
        f"http://127.0.0.1:5001/speak/status/{response['tts_token']}"
    ).json()
    if status["ready"]:
        audio_ready = True
        break
    time.sleep(0.5)

# Option B: Just try to download (returns 202 if not ready yet)
while True:
    result = requests.get(response["audio_url"])
    if result.status_code == 200:
        # Audio ready
        play_audio(result.content)
        break
    elif result.status_code == 202:
        # Still processing
        time.sleep(0.5)
    else:
        # Error
        break
```

## Backend Endpoints

### POST /chat
- **New parameter**: `"tts": true/false` (optional, defaults to false)
- **New response fields**: `tts_token`, `audio_url` (when `tts=true`)
- **Latency**: Returns immediately with text while audio generates in background

### GET /speak/status/{token}
- **Returns**: `{"ready": true/false, "token": "..."}`
- **Purpose**: Poll to check if audio is ready without downloading
- **Status codes**: 
  - 200: Token found
  - 404: Token not found or expired

### GET /speak/audio/{token}.mp3
- **Returns**: Audio file (one-time download, removed from cache after)
- **Status codes**:
  - 200: Audio ready, returns MP3 data
  - 202: Audio still being generated (try again soon)
  - 404: Token not found or expired

## Configuration

TTS provider is determined by character personality:
- `personalities.json` → `character.tts.provider` field
- Supports: `elevenlabs`, `edge-tts`, `gtts`, `macos`
- Falls back gracefully if primary provider unavailable

## Performance Notes

- **Text latency**: 8-15s (LLM only, no TTS blocking)
- **Audio generation**: 2-5s (background, non-blocking)
- **Audio TTL**: 120 seconds (auto-cleaned from cache)
- **Concurrent requests**: Fully supported (each request gets own TTS token)

## Unity Client Integration

**For CompanionController.cs:**

```csharp
// Existing text-chat code
var chatData = new { 
    message = userInput, 
    character = characterId,
    tts = true  // Add this flag
};

// Existing code returns response with tts_token & audio_url
var ttsToken = response["tts_token"];
var audioUrl = response["audio_url"];

// Display text immediately
DisplaySpeechBubble(response["reply"]);

// In background, download and play audio
StartCoroutine(DownloadAndPlayAudioAsync(audioUrl));
```

See [integration example] for full CompanionController updates.

## Backward Compatibility

- Old clients not sending `"tts": true` still work → no token returned
- Existing `/speak` endpoint unchanged (still blocks, returns nothing)
- New async TTS is opt-in via request parameter

## Testing

```bash
# Test async TTS
curl -X POST 'http://127.0.0.1:5001/chat' \
  -H 'Content-Type: application/json' \
  -d '{
    "message": "hello",
    "character": "female_default",
    "tts": true
  }' | jq .

# Expected: reply + tts_token + audio_url (no audio data in response)

# Check audio status
curl 'http://127.0.0.1:5001/speak/status/{tts_token}' | jq .

# Download audio when ready
curl 'http://127.0.0.1:5001/speak/audio/{tts_token}.mp3' -o response.mp3
```

## Troubleshooting

- **No tts_token in response**: Make sure request includes `"tts": true`
- **Audio URL returns 404**: Token expired (2 minute TTL)
- **Audio URL returns 202**: Audio still generating, try again in 1-2 seconds
- **No audio file generated**: Check backend logs for TTS provider errors

---

**Next Steps**: 
1. Update CompanionController.cs to send `tts: true` in chat requests
2. Add async audio download coroutine
3. Test with real voice interactions
4. Monitor performance improvements
