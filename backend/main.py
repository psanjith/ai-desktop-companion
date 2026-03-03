import sqlite3
import json
import os
import re
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

_PROVIDER = _config.get("provider", "ollama").lower()
_MODEL    = _config.get("model", "llama3.2:3b")
print(f"[Config] Provider: {_PROVIDER}  Model: {_MODEL}")

# ── LLM client ─────────────────────────────────────────────────────────────────
_ollama_client = _groq_client = _openai_client = None

if _PROVIDER == "ollama":
    from ollama import Client as _OllamaClient
    _ollama_client = _OllamaClient(host=_config.get("ollama_host", "http://localhost:11434"))
elif _PROVIDER == "groq":
    try:
        from groq import Groq as _Groq
        _groq_client = _Groq(api_key=_config["api_key"])
    except ImportError:
        raise ImportError("[Config] 'groq' package missing. Run: pip install groq")
elif _PROVIDER == "openai":
    try:
        from openai import OpenAI as _OpenAI
        _openai_client = _OpenAI(api_key=_config["api_key"])
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
        model=_MODEL, messages=messages, max_tokens=80, temperature=0.8, stream=stream
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
    conn.commit()
    return conn


def build_messages(personality, history, user_input):
    """Build the Ollama messages list — shared by /chat and /chat/stream."""
    char_name = personality.get("name", "Companion")
    quirks = personality.get("quirks", "")
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
                f"Stay consistent with this personality. "
                f"Respond naturally, like a character, not an assistant. "
                f"But don't overdo the anime style, keep it balanced and natural. "
                f"Keep responses short — 1 to 3 sentences max.\n"
                f"CRITICAL: You MUST include 1-2 physical emotes per reply in *asterisks*.\n"
                f"Use ONLY emotes from this list: {personality.get('emote_list', '*nods* *shrugs* *tilts head*')}\n"
                f"IMPORTANT: Keep emotes SHORT (1-3 words max). "
                f"WRONG: *bounces up and down excitedly in seat* "
                f"RIGHT: *bounces* or *nods*"
            ),
        }
    ]
    messages.append({"role": "user", "content": "What should I call you?"})
    messages.append({"role": "assistant", "content": f"*waves* I'm {char_name}! Nice to meet you~"})
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

    # Load last 2 interactions for THIS character only
    cur.execute("SELECT user, ai FROM memory WHERE character = ? ORDER BY id DESC LIMIT 2", (character,))
    history = cur.fetchall()
    messages = build_messages(personality, history, user_input)

    # Call LLM via configured provider
    response = _llm_call(messages)
    raw_reply = _reply_from_response(response)

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
    return clean_reply, emotes


@app.route("/chat", methods=["POST"])
def chat_api():
    try:
        data = request.get_json()
        user_input = data.get("message", "")
        character = data.get("character", "female_default")
        if not user_input:
            return jsonify({"error": "No message provided"}), 400
        reply, emotes = chat(user_input, character)
        emotion = detect_emotion(reply, emotes)
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

        # Load history and build messages
        cur.execute("SELECT user, ai FROM memory WHERE character = ? ORDER BY id DESC LIMIT 2", (character,))
        history = cur.fetchall()
        messages = build_messages(personality, history, user_input)

        def generate():
            try:
                full_reply = ""
                stream = _llm_call(messages, stream=True)
                for chunk in stream:
                    token = _token_from_chunk(chunk)
                    full_reply += token
                    # Send each token as an SSE data line
                    yield f"data: {json.dumps({'token': token})}\n\n"

                # Stream is done — extract emotes and send final message
                raw_reply = full_reply.strip()
                emotes = re.findall(r'\*([^*]+)\*', raw_reply)
                clean_reply = re.sub(r'\*[^*]+\*', '', raw_reply).strip()
                clean_reply = re.sub(r'\s{2,}', ' ', clean_reply).strip()

                # Save to memory
                cur.execute("INSERT INTO memory (character, user, ai) VALUES (?, ?, ?)",
                            (character, user_input, raw_reply))
                conn.commit()

                # Detect emotion from the full reply
                emotion = detect_emotion(clean_reply, emotes)

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
        conn.commit()
        conn.close()
        return jsonify({"ok": True, "character": character})
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