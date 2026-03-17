import sqlite3
import json
import os
import re
import threading
import tempfile
import urllib.request
import subprocess
import time
import uuid
from flask import Flask, request, jsonify, Response, stream_with_context

# Pre-load Whisper model once at startup — avoids ~2s reload on every /transcribe call
try:
    from faster_whisper import WhisperModel
    _whisper_model = WhisperModel("tiny", device="cpu", compute_type="int8")
    print("[Whisper] Model loaded and ready.")
except Exception as _e:
    _whisper_model = None
    print(f"[Whisper] Failed to load model: {_e}")

# ── Paths ──────────────────────────────────────────────────────────────────────
BASE_DIR          = os.path.dirname(os.path.abspath(__file__))
PERSONALITIES_PATH = os.path.join(BASE_DIR, "personalities.json")
MEMORY_DB_PATH    = os.path.join(BASE_DIR, "memory.db")

# ── Config ─────────────────────────────────────────────────────────────────────
_CONFIG_PATH = os.path.join(BASE_DIR, "config.json")
try:
    with open(_CONFIG_PATH) as _f:
        _config = json.load(_f)
except FileNotFoundError:
    _config = {"provider": "ollama", "model": "llama3.2:3b",
               "ollama_host": "http://localhost:11434"}
    print("[Config] config.json not found — defaulting to Ollama llama3.2:3b")

_PROVIDER = os.environ.get("PROVIDER", _config.get("provider", "ollama")).lower()
_MODEL    = os.environ.get("MODEL", _config.get("model", "llama3.2:3b"))
_OLLAMA_HOST = os.environ.get("OLLAMA_HOST", _config.get("ollama_host", "http://localhost:11434"))
_API_KEY = os.environ.get("API_KEY", _config.get("api_key", ""))
print(f"[Config] Provider: {_PROVIDER}  Model: {_MODEL}")

# ── LLM client ─────────────────────────────────────────────────────────────────
_ollama_client = _groq_client = _openai_client = None

if _PROVIDER == "ollama":
    from ollama import Client as _OllamaClient
    _ollama_client = _OllamaClient(host=_OLLAMA_HOST)
elif _PROVIDER == "groq":
    try:
        from groq import Groq as _Groq
        if not _API_KEY:
            raise ValueError("[Config] Missing API key. Set API_KEY env var or config.json api_key")
        _groq_client = _Groq(api_key=_API_KEY)
    except ImportError:
        raise ImportError("[Config] 'groq' package missing. Run: pip install groq")
elif _PROVIDER == "openai":
    try:
        from openai import OpenAI as _OpenAI
        if not _API_KEY:
            raise ValueError("[Config] Missing API key. Set API_KEY env var or config.json api_key")
        _openai_client = _OpenAI(api_key=_API_KEY)
    except ImportError:
        raise ImportError("[Config] 'openai' package missing. Run: pip install openai")
else:
    raise ValueError(f"[Config] Unknown provider '{_PROVIDER}'. Use: ollama, groq, or openai")


def _llm_call(messages, stream=False):
    """Route to the configured LLM provider."""
    if _PROVIDER == "ollama":
        return _ollama_client.chat(
            model=_MODEL, messages=messages, stream=stream,
            options={"num_predict": 80, "temperature": 0.8, "num_ctx": 1024}
        )
    client = _groq_client if _PROVIDER == "groq" else _openai_client
    return client.chat.completions.create(
        model=_MODEL, messages=messages, max_tokens=120, temperature=0.8, stream=stream
    )


def _token_from_chunk(chunk) -> str:
    """Extract a text token from a streaming chunk."""
    if _PROVIDER == "ollama":
        return chunk["message"]["content"]
    return chunk.choices[0].delta.content or ""


def _reply_from_response(response) -> str:
    """Extract text from a non-streaming response."""
    if _PROVIDER == "ollama":
        return response["message"]["content"].strip()
    return response.choices[0].message.content.strip()


# ── Emotion tag parser ────────────────────────────────────────────────────────
_EMOTION_RE = re.compile(r'\[e:(joy|fun|angry|sorrow|neutral)\]', re.IGNORECASE)

def _parse_emotion_tag(text: str):
    """Extract [e:emotion] tag that the LLM appends. Returns (emotion|None, cleaned_text)."""
    m = _EMOTION_RE.search(text)
    if m:
        return m.group(1).lower(), _EMOTION_RE.sub('', text).strip()
    return None, text


def _clean_tts_text(text: str) -> str:
    """Light cleanup for speech synthesis while keeping natural phrasing."""
    if not text:
        return ""
    # Strip control tags if any leak through
    cleaned = _EMOTION_RE.sub("", text)
    cleaned = re.sub(r'\*[^*]+\*', '', cleaned)
    # Keep punctuation/prosody cues; only normalize whitespace
    cleaned = re.sub(r'\s{2,}', ' ', cleaned).strip()
    return cleaned


def _clamp01(x: float) -> float:
    return max(0.0, min(1.0, float(x)))


def _voice_settings_for_emotion(tts: dict, emotion: str):
    """Emotion → small prosody offsets for more lifelike delivery."""
    stability = float(tts.get("stability", 0.35))
    similarity = float(tts.get("similarity_boost", 0.85))
    style = float(tts.get("style", 0.35))
    emo = (emotion or "neutral").lower()

    if emo == "joy":
        style += 0.10
        stability -= 0.06
    elif emo == "fun":
        style += 0.14
        stability -= 0.08
    elif emo == "sorrow":
        style -= 0.10
        stability += 0.10
    elif emo == "angry":
        style += 0.06
        stability += 0.03

    return {
        "stability": _clamp01(stability),
        "similarity_boost": _clamp01(similarity),
        "style": _clamp01(style),
        "use_speaker_boost": bool(tts.get("use_speaker_boost", True)),
    }


def _get_desktop_context():
    """Best-effort foreground app context (macOS only)."""
    app = ""
    title = ""
    if os.name != "posix":
        return {"app": app, "title": title}

    try:
        app = subprocess.check_output(
            [
                "osascript",
                "-e",
                'tell application "System Events" to get name of first process whose frontmost is true',
            ],
            text=True,
            timeout=1.5,
        ).strip()
    except Exception:
        app = ""

    try:
        title = subprocess.check_output(
            [
                "osascript",
                "-e",
                'tell application "System Events" to tell (first process whose frontmost is true) to get name of front window',
            ],
            text=True,
            timeout=1.5,
        ).strip()
    except Exception:
        title = ""

    return {"app": app, "title": title}


def _load_personalities():
    with open(PERSONALITIES_PATH, "r") as f:
        return json.load(f)


def _get_personality(character: str):
    all_personalities = _load_personalities()
    personality = all_personalities.get(character)
    if not personality:
        personality = list(all_personalities.values())[0]
    return personality


# ── Long-term user facts ──────────────────────────────────────────────────────
def _extract_facts_bg(character: str, user_msg: str):
    """Background thread: ask LLM to pull personal facts from the user's message and save them."""
    try:
        extr_messages = [
            {
                "role": "system",
                "content": (
                    "You are a fact extractor. Read the user message and extract any memorable "
                    "personal facts (name, job, hobbies, favourite things, pets, etc.).\n"
                    "Reply ONLY with a JSON array of short strings, e.g. [\"name is Alex\", \"likes Python\"].\n"
                    "If there are no personal facts, reply with [].\n"
                    "Max 3 facts. Each fact must be under 80 characters."
                )
            },
            {"role": "user", "content": user_msg}
        ]
        resp = _llm_call(extr_messages, stream=False)
        raw = _reply_from_response(resp).strip()
        m = re.search(r'\[.*?\]', raw, re.DOTALL)
        if not m:
            return
        facts_list = json.loads(m.group())
        if not isinstance(facts_list, list) or not facts_list:
            return
        conn = sqlite3.connect(MEMORY_DB_PATH)
        conn.execute("""
            CREATE TABLE IF NOT EXISTS facts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                character TEXT NOT NULL,
                fact TEXT NOT NULL,
                UNIQUE(character, fact)
            )
        """)
        saved = 0
        for fact in facts_list[:3]:
            if isinstance(fact, str) and 3 < len(fact) < 120:
                conn.execute(
                    "INSERT OR IGNORE INTO facts (character, fact) VALUES (?, ?)",
                    (character, fact.strip())
                )
                saved += 1
        conn.commit()
        conn.close()
        if saved:
            print(f"[Facts] Stored {saved} fact(s) for {character}: {facts_list[:3]}")
    except Exception as exc:
        print(f"[Facts] Extraction error: {exc}")

# Flask app
app = Flask(__name__)


def detect_emotion(text, emotes):
    """Detect the dominant emotion from reply text and emotes.
    Returns: joy, angry, sorrow, fun, or neutral."""
    lower = text.lower()
    emote_str = " ".join(emotes).lower() if emotes else ""
    combined = lower + " " + emote_str

    joy_words = ["haha", "laugh", "giggle", "happy", "excited", "yay", "awesome",
                 "great", "love", "!!", "woohoo", "cheer", "jump", "bounce", "wave"]
    angry_words = ["angry", "grr", "ugh", "annoy", "frustrat", "mad", "hate",
                   "shake", "frown", "no way"]
    sad_words = ["sad", "sigh", "sorry", "miss", "lonely", "cry", "tear",
                 "unfortunate", "sorrow", "down"]
    fun_words = ["tease", "wink", "blush", "shy", "playful", "heh", "wiggle",
                 "poke", "cheeky", "fluster", "embarrass", "fun", "silly"]

    scores = {
        "joy": sum(1 for w in joy_words if w in combined),
        "angry": sum(1 for w in angry_words if w in combined),
        "sorrow": sum(1 for w in sad_words if w in combined),
        "fun": sum(1 for w in fun_words if w in combined),
    }

    best = max(scores, key=scores.get)
    if scores[best] == 0:
        # Fallback: check punctuation/energy
        if combined.count("!") >= 2:
            return "joy"
        if "?" in combined and "..." in combined:
            return "fun"
        return "neutral"
    return best


def get_db():
    """Open a fresh SQLite connection per request (thread-safe)."""
    conn = sqlite3.connect(MEMORY_DB_PATH)
    conn.execute("""
        CREATE TABLE IF NOT EXISTS memory (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            character TEXT DEFAULT 'female_default',
            user TEXT,
            ai TEXT
        )
    """)
    conn.execute("""
        CREATE TABLE IF NOT EXISTS facts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            character TEXT NOT NULL,
            fact TEXT NOT NULL,
            UNIQUE(character, fact)
        )
    """)
    conn.commit()
    return conn


def build_messages(personality, history, user_input, facts=None):
    """Build the LLM messages list — shared by /chat and /chat/stream."""
    char_name = personality.get("name", "Companion")
    quirks = personality.get("quirks", "")

    facts_section = ""
    if facts:
        facts_str = "\n".join(f"- {f}" for f in facts[:10])
        facts_section = (
            f"\nThings you know about the user "
            f"(weave these in naturally when relevant, never force it):\n{facts_str}\n"
        )

    messages = [
        {
            "role": "system",
            "content": (
                f"Your name is {char_name}. You are an anime-style desktop companion.\n"
                f"IMPORTANT: Your name is {char_name}, never use any other name.\n"
                f"Tone: {personality['tone']}. "
                f"Energy: {personality['energy']}. "
                f"Humor: {personality['humor']}. "
                f"Verbosity: {personality['verbosity']}.\n"
                f"Personality: {quirks}\n"
                f"{facts_section}"
                f"Stay consistent with this personality. "
                f"Respond naturally, like a character, not an assistant. "
                f"But don't overdo the anime style, keep it balanced and natural. "
                f"Keep responses short — 1 to 3 sentences max.\n"
                f"CRITICAL: You MUST include 1-2 physical emotes per reply in *asterisks*.\n"
                f"Use ONLY emotes from this list: {personality.get('emote_list', '*nods* *shrugs* *tilts head*')}\n"
                f"IMPORTANT: Keep emotes SHORT (1-3 words max). "
                f"WRONG: *bounces up and down excitedly in seat* "
                f"RIGHT: *bounces* or *nods*\n"
                f"REQUIRED: End every reply with exactly one emotion tag on its own: "
                f"[e:joy] [e:fun] [e:angry] [e:sorrow] or [e:neutral]"
            ),
        }
    ]
    messages.append({"role": "user", "content": "What should I call you?"})
    messages.append({"role": "assistant", "content": f"*waves* I'm {char_name}! Nice to meet you~ [e:joy]"})
    for u, a in reversed(history):
        messages.append({"role": "user", "content": u})
        messages.append({"role": "assistant", "content": a})
    messages.append({"role": "user", "content": user_input})
    return messages


def chat(user_input, character="female_default"):
    conn = get_db()
    cur = conn.cursor()

    # Load all personalities fresh each time
    with open(PERSONALITIES_PATH, "r") as f:
        all_personalities = json.load(f)

    # Get personality for current character (fallback to first)
    personality = all_personalities.get(character)
    if not personality:
        personality = list(all_personalities.values())[0]

    # Load last 8 interactions for THIS character only
    cur.execute("SELECT user, ai FROM memory WHERE character = ? ORDER BY id DESC LIMIT 8", (character,))
    history = cur.fetchall()

    # Load long-term user facts and inject into prompt
    facts = [r[0] for r in cur.execute(
        "SELECT fact FROM facts WHERE character = ? ORDER BY id DESC LIMIT 10", (character,)
    ).fetchall()]
    messages = build_messages(personality, history, user_input, facts=facts)

    # Call LLM via configured provider
    response = _llm_call(messages)
    raw_reply = _reply_from_response(response)

    # Parse structured emotion tag — more reliable than keyword scoring
    emotion_from_tag, raw_reply = _parse_emotion_tag(raw_reply)

    # Extract emotes like *yawns*, *blinks slowly* from reply
    emotes = re.findall(r'\*([^*]+)\*', raw_reply)
    # Clean reply — remove emote markers for display
    clean_reply = re.sub(r'\*[^*]+\*', '', raw_reply).strip()
    # Clean up extra whitespace/punctuation left behind
    clean_reply = re.sub(r'\s{2,}', ' ', clean_reply).strip()

    # Save to memory (keep raw version so LLM sees its own style)
    cur.execute("INSERT INTO memory (character, user, ai) VALUES (?, ?, ?)", (character, user_input, raw_reply))
    conn.commit()
    conn.close()

    # Extract facts in background — zero latency impact on response
    if len(user_input) > 15:
        threading.Thread(target=_extract_facts_bg, args=(character, user_input), daemon=True).start()

    return clean_reply, emotes, emotion_from_tag


@app.route("/chat", methods=["POST"])
def chat_api():
    try:
        data = request.get_json()
        user_input = data.get("message", "")
        character = data.get("character", "female_default")
        if not user_input:
            return jsonify({"error": "No message provided"}), 400
        reply, emotes, emotion_tag = chat(user_input, character)
        emotion = emotion_tag or detect_emotion(reply, emotes)
        return jsonify({"reply": reply, "emotes": emotes, "emotion": emotion})
    except Exception as e:
        import traceback
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500


@app.route("/chat/stream", methods=["POST"])
def chat_stream_api():
    """Stream tokens as Server-Sent Events (SSE) for real-time display."""
    try:
        data = request.get_json()
        user_input = data.get("message", "")
        character = data.get("character", "female_default")
        if not user_input:
            return jsonify({"error": "No message provided"}), 400

        conn = get_db()
        cur = conn.cursor()

        # Load personality
        with open(PERSONALITIES_PATH, "r") as f:
            all_personalities = json.load(f)
        personality = all_personalities.get(character)
        if not personality:
            personality = list(all_personalities.values())[0]

        # Load history and build messages (last 8 exchanges)
        cur.execute("SELECT user, ai FROM memory WHERE character = ? ORDER BY id DESC LIMIT 8", (character,))
        history = cur.fetchall()

        # Load long-term user facts and inject into prompt
        facts = [r[0] for r in cur.execute(
            "SELECT fact FROM facts WHERE character = ? ORDER BY id DESC LIMIT 10", (character,)
        ).fetchall()]
        messages = build_messages(personality, history, user_input, facts=facts)

        def generate():
            try:
                full_reply = ""
                stream = _llm_call(messages, stream=True)
                for chunk in stream:
                    token = _token_from_chunk(chunk)
                    full_reply += token
                    # Send each token as an SSE data line
                    yield f"data: {json.dumps({'token': token})}\n\n"

                # Stream is done — parse emotion tag, extract emotes, build clean reply
                raw_reply = full_reply.strip()
                emotion_from_tag, raw_reply = _parse_emotion_tag(raw_reply)
                emotes = re.findall(r'\*([^*]+)\*', raw_reply)
                clean_reply = re.sub(r'\*[^*]+\*', '', raw_reply).strip()
                clean_reply = re.sub(r'\s{2,}', ' ', clean_reply).strip()

                # Save to memory
                cur.execute("INSERT INTO memory (character, user, ai) VALUES (?, ?, ?)",
                            (character, user_input, raw_reply))
                conn.commit()

                # Prefer LLM-supplied tag; fall back to keyword scoring
                emotion = emotion_from_tag or detect_emotion(clean_reply, emotes)

                # Extract facts in background — no latency on the stream
                if len(user_input) > 15:
                    threading.Thread(target=_extract_facts_bg, args=(character, user_input), daemon=True).start()

                # Send final event with emotes, emotion, and clean reply
                yield f"data: {json.dumps({'done': True, 'reply': clean_reply, 'emotes': emotes, 'emotion': emotion})}\n\n"
            finally:
                conn.close()

        return Response(
            stream_with_context(generate()),
            mimetype="text/event-stream",
            headers={
                "Cache-Control": "no-cache",
                "X-Accel-Buffering": "no",
            }
        )
    except Exception as e:
        import traceback
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500


@app.route("/transcribe", methods=["POST"])
def transcribe_audio():
    """Receive a WAV audio file from Unity and return transcribed text via faster-whisper."""
    try:
        import time
        import io

        if _whisper_model is None:
            return jsonify({"error": "Whisper model not loaded"}), 500

        if "audio" not in request.files:
            return jsonify({"error": "No audio file provided"}), 400

        audio_file = request.files["audio"]

        # Read directly into a BytesIO buffer — no temp file write/read round-trip
        audio_bytes = io.BytesIO(audio_file.read())

        t0 = time.time()
        segments, _ = _whisper_model.transcribe(
            audio_bytes,
            language="en",
            beam_size=1,            # greedy decoding — fastest
            best_of=1,              # no sampling candidates
            condition_on_previous_text=False,  # skip inter-segment context (not needed for one-shot clips)
            vad_filter=True,        # skip silent regions before decoding
            vad_parameters=dict(min_silence_duration_ms=300),
        )
        # Materialise the lazy generator eagerly (avoids leaving model in use)
        transcript = " ".join(seg.text.strip() for seg in segments).strip()
        t1 = time.time()

        print(f"[Transcribe] '{transcript}' ({t1-t0:.2f}s)")
        return jsonify({"text": transcript})

    except Exception as e:
        import traceback
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500


@app.route("/memory/clear", methods=["POST"])
def memory_clear():
    """Clear conversation memory for a character."""
    try:
        data = request.get_json() or {}
        character = data.get("character", "female_default")
        conn = get_db()
        conn.execute("DELETE FROM memory WHERE character = ?", (character,))
        conn.execute("DELETE FROM facts  WHERE character = ?", (character,))
        conn.commit()
        conn.close()
        return jsonify({"ok": True, "character": character})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


@app.route("/facts", methods=["GET"])
def get_facts():
    """Return stored long-term user facts for a character."""
    try:
        character = request.args.get("character", "female_default")
        conn = get_db()
        rows = conn.execute(
            "SELECT id, fact FROM facts WHERE character = ? ORDER BY id", (character,)
        ).fetchall()
        conn.close()
        return jsonify({"character": character, "facts": [{"id": r[0], "fact": r[1]} for r in rows]})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


# Process handle so we can kill previous speech before starting new one
_speak_proc = None
_speak_audio_path = None
_tts_audio_cache = {}
_tts_audio_cache_lock = threading.Lock()
_TTS_AUDIO_TTL_SECONDS = 120


def _cleanup_audio_file():
    global _speak_audio_path
    if _speak_audio_path and os.path.exists(_speak_audio_path):
        try:
            os.remove(_speak_audio_path)
        except Exception:
            pass
    _speak_audio_path = None


def _purge_tts_audio_cache(now: float = None):
    if now is None:
        now = time.time()
    expired = [k for k, v in _tts_audio_cache.items() if v["expires_at"] <= now]
    for k in expired:
        _tts_audio_cache.pop(k, None)


def _store_tts_audio(audio_bytes: bytes, mime: str = "audio/mpeg") -> str:
    token = uuid.uuid4().hex
    with _tts_audio_cache_lock:
        _purge_tts_audio_cache()
        _tts_audio_cache[token] = {
            "audio": audio_bytes,
            "mime": mime,
            "expires_at": time.time() + _TTS_AUDIO_TTL_SECONDS,
        }
    return token


def _voice_profile_for(character: str):
    personality = _get_personality(character)
    tts = personality.get("tts", {})
    provider = (tts.get("provider") or _config.get("tts_provider") or "macos").lower()
    return personality, tts, provider


def _generate_elevenlabs_audio_bytes(text: str, tts: dict, emotion: str = "neutral"):
    # Env var takes priority so Render can override any baked-in config
    api_key = (os.environ.get("ELEVENLABS_API_KEY")
               or tts.get("elevenlabs_api_key")
               or _config.get("elevenlabs_api_key"))
    voice_id = tts.get("elevenlabs_voice_id")
    if not api_key or not voice_id:
        raise ValueError("ElevenLabs is selected, but API key or voice id is missing")

    payload = {
        "text": text,
        "model_id": tts.get("elevenlabs_model", "eleven_multilingual_v2"),
        "voice_settings": _voice_settings_for_emotion(tts, emotion),
    }

    req = urllib.request.Request(
        url=f"https://api.elevenlabs.io/v1/text-to-speech/{voice_id}",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "xi-api-key": api_key,
            "Content-Type": "application/json",
            "Accept": "audio/mpeg",
        },
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=25) as resp:
            audio_bytes = resp.read()
        return audio_bytes, voice_id
    except urllib.error.HTTPError as exc:
        try:
            detail = exc.read().decode("utf-8", errors="ignore")[:300]
        except Exception:
            detail = ""
        raise ValueError(f"ElevenLabs API error {exc.code}: {detail or exc.reason}")


def _speak_macos(text: str, character: str, tts: dict, emotion: str = "neutral"):
    global _speak_proc
    import subprocess

    fallback_voice = "Samantha" if "female" in character else "Daniel"
    voice = tts.get("macos_voice", fallback_voice)
    rate = int(tts.get("rate", 175))
    emo = (emotion or "neutral").lower()
    if emo == "sorrow":
        rate -= 12
    elif emo == "joy":
        rate += 10
    elif emo == "fun":
        rate += 14
    elif emo == "angry":
        rate += 4
    rate = max(120, min(245, rate))
    _speak_proc = subprocess.Popen(["say", "-v", voice, "-r", str(rate), text])
    return {"provider": "macos", "voice": voice, "rate": rate}


def _speak_elevenlabs(text: str, tts: dict, emotion: str = "neutral"):
    global _speak_proc, _speak_audio_path
    import subprocess
    audio_bytes, voice_id = _generate_elevenlabs_audio_bytes(text, tts, emotion=emotion)

    _cleanup_audio_file()
    fd, path = tempfile.mkstemp(prefix="companion_tts_", suffix=".mp3")
    with os.fdopen(fd, "wb") as f:
        f.write(audio_bytes)
    _speak_audio_path = path

    _speak_proc = subprocess.Popen(["afplay", path])
    return {"provider": "elevenlabs", "voice_id": voice_id}


@app.route("/speak/audio/<token>.mp3", methods=["GET"])
def speak_audio(token: str):
    """One-time downloadable TTS audio for client-side playback."""
    with _tts_audio_cache_lock:
        _purge_tts_audio_cache()
        item = _tts_audio_cache.pop(token, None)
    if not item:
        return jsonify({"ok": False, "error": "Audio not found or expired"}), 404
    return Response(item["audio"], mimetype=item["mime"])


@app.route("/debug/tts-key", methods=["GET"])
def debug_tts_key():
    """Debug: confirm ElevenLabs key is present (never logs full key)."""
    key = os.environ.get("ELEVENLABS_API_KEY", "")
    if not key:
        return jsonify({"found": False, "note": "ELEVENLABS_API_KEY env var not set"})
    return jsonify({
        "found": True,
        "length": len(key),
        "starts_with": key[:7],
        "ends_with": key[-6:],
        "has_whitespace": key != key.strip(),
    })


@app.route("/speak", methods=["POST"])
def speak():
    """Speak text with per-character voice profiles (ElevenLabs or macOS fallback)."""
    global _speak_proc
    import sys
    try:
        data = request.get_json() or {}
        text = _clean_tts_text(data.get("text", ""))
        character = data.get("character", "female_default")
        emotion = (data.get("emotion") or "neutral").lower()
        if not text:
            return jsonify({"ok": False, "error": "No text"}), 400

        _, tts, provider = _voice_profile_for(character)

        # Hosted/Linux path: generate audio and let Unity play it locally
        if sys.platform != "darwin":
            try:
                if provider == "elevenlabs":
                    audio_bytes, voice_id = _generate_elevenlabs_audio_bytes(text, tts, emotion=emotion)
                    token = _store_tts_audio(audio_bytes, mime="audio/mpeg")
                    audio_url = request.host_url.rstrip("/") + f"/speak/audio/{token}.mp3"
                    return jsonify({
                        "ok": True,
                        "emotion": emotion,
                        "tts": {"provider": "elevenlabs", "voice_id": voice_id, "mode": "client_playback"},
                        "audio_url": audio_url,
                        "audio_mime": "audio/mpeg",
                    })
                return jsonify({"ok": False, "note": "Cloud TTS needs provider=elevenlabs for client playback"})
            except Exception as exc:
                return jsonify({"ok": False, "error": str(exc), "note": "Cloud TTS generation failed"}), 500

        # Kill previous speech if still running
        if _speak_proc and _speak_proc.poll() is None:
            _speak_proc.terminate()
        _cleanup_audio_file()

        # Prefer profile provider, but gracefully fall back to high-quality local voice
        used = None
        if provider == "elevenlabs":
            try:
                used = _speak_elevenlabs(text, tts, emotion=emotion)
            except Exception as exc:
                print(f"[TTS] ElevenLabs failed, falling back to macOS voice: {exc}")
                used = _speak_macos(text, character, tts, emotion=emotion)
                used["fallback_reason"] = str(exc)
        else:
            used = _speak_macos(text, character, tts, emotion=emotion)

        return jsonify({"ok": True, "tts": used, "emotion": emotion})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


@app.route("/speak/stop", methods=["POST"])
def speak_stop():
    """Kill any in-progress TTS speech immediately."""
    global _speak_proc
    import sys
    if sys.platform != "darwin":
        return jsonify({"ok": True, "note": "No-op on hosted backend; client handles local audio stop"})
    if _speak_proc and _speak_proc.poll() is None:
        _speak_proc.terminate()
        _speak_proc = None
    _cleanup_audio_file()
    return jsonify({"ok": True})


@app.route("/idle", methods=["GET"])
def idle_message():
    """Generate a short proactive check-in message for a character who hasn't spoken in a while."""
    try:
        character = request.args.get("character", "female_default")
        with open(PERSONALITIES_PATH, "r") as f:
            all_personalities = json.load(f)
        personality = all_personalities.get(character)
        if not personality:
            personality = list(all_personalities.values())[0]
        desktop_ctx = _get_desktop_context()
        active_app = (request.args.get("app") or desktop_ctx.get("app") or "").strip()
        active_title = (desktop_ctx.get("title") or "").strip()

        app_hint = ""
        app_l = active_app.lower()
        if "code" in app_l or "cursor" in app_l or "xcode" in app_l:
            app_hint = "The user appears to be coding. You can make a brief coding-friendly check-in."
        elif "safari" in app_l or "chrome" in app_l or "firefox" in app_l or "brave" in app_l:
            app_hint = "The user appears to be browsing. You can make a brief web-browsing-themed check-in."
        elif "terminal" in app_l or "iterm" in app_l:
            app_hint = "The user appears to be in terminal. You can make a brief hacker-ish check-in."

        char_name  = personality.get("name", "Companion")
        quirks     = personality.get("quirks", "")
        emote_list = personality.get("emote_list", "*nods* *shrugs* *tilts head*")
        messages = [
            {
                "role": "system",
                "content": (
                    f"Your name is {char_name}. You are an anime-style desktop companion.\n"
                    f"Tone: {personality['tone']}. Energy: {personality['energy']}.\n"
                    f"Personality: {quirks}\n"
                    f"Active app: {active_app or 'unknown'}\n"
                    f"Active window title: {active_title or 'unknown'}\n"
                    f"{app_hint}\n"
                    f"The user hasn't said anything for a while. "
                    f"Say something short and natural to check in — stay fully in character, "
                    f"not like an assistant. ONE sentence max. "
                    f"Include exactly 1 emote from: {emote_list}"
                )
            },
            {"role": "user", "content": "[idle check-in]"}
        ]
        response = _llm_call(messages)
        raw = _reply_from_response(response)
        emotes = re.findall(r'\*([^*]+)\*', raw)
        clean  = re.sub(r'\*[^*]+\*', '', raw).strip()
        clean  = re.sub(r'\s{2,}', ' ', clean).strip()
        emotion = detect_emotion(clean, emotes)
        return jsonify({"reply": clean, "emotes": emotes, "emotion": emotion})
    except Exception as e:
        import traceback
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500


@app.route("/desktop/context", methods=["GET"])
def desktop_context():
    """Return lightweight foreground app context to help proactive reactions."""
    try:
        ctx = _get_desktop_context()
        return jsonify(ctx)
    except Exception as e:
        return jsonify({"error": str(e)}), 500


@app.route("/character", methods=["GET"])
def character_info():
    """Return personality info for a character (used by Unity for display name)."""
    try:
        character = request.args.get("id", "female_default")
        with open(PERSONALITIES_PATH, "r") as f:
            all_personalities = json.load(f)
        personality = all_personalities.get(character, {})
        return jsonify({
            "name": personality.get("name", character),
            "tone": personality.get("tone", "neutral"),
        })
    except Exception as e:
        return jsonify({"name": character}), 200


if __name__ == "__main__":
    print("AI Server is running on port 5001...")
    app.run(host="127.0.0.1", port=5001)