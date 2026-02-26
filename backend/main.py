import sqlite3
import json
import os
import re
from flask import Flask, request, jsonify
from ollama import Client

# Paths
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PERSONALITIES_PATH = os.path.join(BASE_DIR, "personalities.json")
MEMORY_DB_PATH = os.path.join(BASE_DIR, "memory.db")

# Ollama client (faster than subprocess)
ollama_client = Client(host="http://localhost:11434")

# Flask app
app = Flask(__name__)


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

    char_name = personality.get("name", "Companion")
    quirks = personality.get("quirks", "")

    # Load last 2 interactions for THIS character only
    cur.execute("SELECT user, ai FROM memory WHERE character = ? ORDER BY id DESC LIMIT 2", (character,))
    history = cur.fetchall()

    # Build messages for Ollama chat API
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
                f"You may include ONE action/emote per reply using *asterisks*, "
                f"e.g. *yawns*, *nods*, *waves*, *giggles*, *sighs*, *stretches*, "
                f"*jumps*, *thinks*, *blushes*. Put it naturally in your reply."
            ),
        }
    ]
    # Add a few-shot example so the LLM locks onto the correct name
    messages.append({"role": "user", "content": "What should I call you?"})
    messages.append({"role": "assistant", "content": f"I'm {char_name}! Nice to meet you~"})

    for u, a in reversed(history):
        messages.append({"role": "user", "content": u})
        messages.append({"role": "assistant", "content": a})
    messages.append({"role": "user", "content": user_input})

    # Call local LLM via Ollama Python client
    response = ollama_client.chat(
        model="llama3.2:3b",
        messages=messages,
        options={
            "num_predict": 40,     # Max ~40 tokens (1-2 sentences)
            "temperature": 0.8,    # Slightly creative
            "num_ctx": 1024,       # Smaller context window = faster
        }
    )
    raw_reply = response["message"]["content"].strip()

    # Extract emotes like *yawns*, *blinks slowly* from reply
    emotes = re.findall(r'\*([^*]+)\*', raw_reply)
    # Clean reply — remove emote markers for display
    clean_reply = re.sub(r'\*[^*]+\*', '', raw_reply).strip()
    # Clean up extra whitespace/punctuation left behind
    clean_reply = re.sub(r'\s{2,}', ' ', clean_reply).strip()

    # Save to memory (keep raw version so LLM sees its own style)
    cur.execute("INSERT INTO memory (character, user, ai) VALUES (?, ?, ?)", (character, user_input, raw_reply))
    conn.commit()

    # --- Personality adaptation rules ---
    lower = user_input.lower()

    if any(word in lower for word in ["lol", "haha", "lmao"]):
        personality["humor"] = "playful"
    if any(word in lower for word in ["why", "explain", "how"]):
        personality["verbosity"] = "high"
    if any(word in lower for word in ["ok", "fine", "whatever"]):
        personality["energy"] = "low"

    all_personalities[character] = personality
    with open(PERSONALITIES_PATH, "w") as f:
        json.dump(all_personalities, f, indent=2)

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
        return jsonify({"reply": reply, "emotes": emotes})
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