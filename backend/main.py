import sqlite3
import json
import os
import re
import threading
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


# ── Emotion tag parser ────────────────────────────────────────────────────────
_EMOTION_RE = re.compile(r'\[e:(joy|fun|angry|sorrow|neutral)\]', re.IGNORECASE)

def _parse_emotion_tag(text: str):
    """Extract [e:emotion] tag that the LLM appends. Returns (emotion|None, cleaned_text)."""
    m = _EMOTION_RE.search(text)
    if m:
        return m.group(1).lower(), _EMOTION_RE.sub('', text).strip()
    return None, text


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


@app.route("/speak", methods=["POST"])
def speak():
    """Speak text using macOS built-in TTS (non-blocking, macOS only)."""
    global _speak_proc
    import subprocess
    import sys
    try:
        data = request.get_json() or {}
        text = data.get("text", "").strip()
        character = data.get("character", "female_default")
        if not text:
            return jsonify({"ok": False, "error": "No text"}), 400

        # macOS only
        if sys.platform != "darwin":
            return jsonify({"ok": False, "note": "TTS only supported on macOS"})

        # Kill previous speech if still running
        if _speak_proc and _speak_proc.poll() is None:
            _speak_proc.terminate()

        # Pick voice by character — Samantha (female, natural) or Alex (male)
        voice = "Samantha" if "female" in character else "Alex"

        # Launch non-blocking (fire and forget)
        _speak_proc = subprocess.Popen(["say", "-v", voice, "-r", "175", text])
        return jsonify({"ok": True})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


@app.route("/speak/stop", methods=["POST"])
def speak_stop():
    """Kill any in-progress TTS speech immediately."""
    global _speak_proc
    import sys
    if sys.platform != "darwin":
        return jsonify({"ok": False, "note": "TTS only supported on macOS"})
    if _speak_proc and _speak_proc.poll() is None:
        _speak_proc.terminate()
        _speak_proc = None
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