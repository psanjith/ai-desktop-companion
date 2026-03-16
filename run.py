#!/usr/bin/env python
import sys
import os

# Add the backend directory to path so imports work
sys.path.insert(0, os.path.join(os.path.dirname(__file__), 'backend'))

# Import and run the Flask app
from main import app

if __name__ == "__main__":
    # Render will set PORT env var, default to 5001 for local testing
    port = int(os.environ.get("PORT", 5001))
    # Bind to 0.0.0.0 so Render can access it
    app.run(host="0.0.0.0", port=port)
