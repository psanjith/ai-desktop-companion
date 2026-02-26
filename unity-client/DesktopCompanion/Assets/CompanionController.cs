using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;

public class CompanionController : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField userInputField;
    public TMP_Text speechBubbleText;
    public GameObject speechBubble;
    public TMP_Text characterNameText; // Optional: shows current character name

    private string apiUrl = "http://127.0.0.1:5001/chat";

    void Start()
    {
        if (speechBubble != null)
            speechBubble.SetActive(false);
        else
            Debug.LogWarning("CompanionController: speechBubble is not assigned!");

        if (userInputField == null)
            Debug.LogWarning("CompanionController: userInputField is not assigned!");

        if (speechBubbleText == null)
            Debug.LogWarning("CompanionController: speechBubbleText is not assigned!");

        // Make input field text bigger and easier to read
        if (userInputField != null)
        {
            // Bigger font
            userInputField.textComponent.fontSize = 20;
            userInputField.textComponent.color = Color.white;

            // Bigger placeholder text too
            if (userInputField.placeholder != null)
            {
                var placeholderText = userInputField.placeholder.GetComponent<TMP_Text>();
                if (placeholderText != null)
                {
                    placeholderText.fontSize = 20;
                    placeholderText.text = "Type here...";
                }
            }

            // Make input background semi-transparent dark
            var inputImage = userInputField.GetComponent<UnityEngine.UI.Image>();
            if (inputImage != null)
            {
                inputImage.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            }
        }

        // Make speech bubble semi-transparent
        if (speechBubble != null)
        {
            var bubbleImage = speechBubble.GetComponent<Image>();
            if (bubbleImage != null)
            {
                bubbleImage.color = new Color(1f, 1f, 1f, 0.85f);
            }
        }

        // Set up text to auto-size within the bubble
        if (speechBubbleText != null)
        {
            speechBubbleText.enableWordWrapping = true;
            speechBubbleText.overflowMode = TextOverflowModes.Ellipsis;
            speechBubbleText.enableAutoSizing = true;
            speechBubbleText.fontSizeMin = 10;
            speechBubbleText.fontSizeMax = 20;
            speechBubbleText.alignment = TextAlignmentOptions.TopLeft;
            speechBubbleText.margin = new Vector4(8, 5, 8, 5);
        }

        // Make Canvas scale with screen size so everything resizes with the window
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(800, 600);
                scaler.matchWidthOrHeight = 0.5f; // Balanced scaling
            }
        }

        UpdateCharacterNameUI();
    }

    /// <summary>
    /// Called by the Switch Character button.
    /// </summary>
    public void OnSwitchCharacter()
    {
        if (CharacterManager.Instance != null)
        {
            CharacterManager.Instance.SwitchCharacter();

            // Clear the speech bubble when switching characters
            if (speechBubbleText != null)
                speechBubbleText.text = "";
            if (speechBubble != null)
                speechBubble.SetActive(false);

            UpdateCharacterNameUI();
        }
    }

    private void UpdateCharacterNameUI()
    {
        if (characterNameText != null && CharacterManager.Instance != null)
        {
            // Fetch display name from backend
            StartCoroutine(FetchCharacterName());
        }
    }

    IEnumerator FetchCharacterName()
    {
        string charId = CharacterManager.Instance.GetCurrentCharacterName();
        string url = $"http://127.0.0.1:5001/character?id={charId}";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var info = JsonUtility.FromJson<CharacterInfo>(req.downloadHandler.text);
                if (characterNameText != null)
                    characterNameText.text = info.name;
            }
            else
            {
                // Fallback to prefab name
                if (characterNameText != null)
                    characterNameText.text = charId;
            }
        }
    }

    public void OnSendMessage()
    {
        if (userInputField == null) return;
        string message = userInputField.text.Trim();
        if (string.IsNullOrEmpty(message)) return;
        userInputField.text = "";
        StartCoroutine(SendToAI(message));
    }

    IEnumerator SendToAI(string message)
    {
        if (speechBubbleText != null)
            speechBubbleText.text = "...";
        if (speechBubble != null)
            speechBubble.SetActive(true);

        // Send current character so backend uses the right personality
        string charName = CharacterManager.Instance != null
            ? CharacterManager.Instance.GetCurrentCharacterName()
            : "female_default";
        string jsonBody = JsonUtility.ToJson(new ChatRequest { message = message, character = charName });
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest req = new UnityWebRequest(apiUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                ChatResponse res = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
                if (speechBubbleText != null)
                    speechBubbleText.text = res.reply;

                // Trigger emote animations on the character
                if (res.emotes != null && res.emotes.Length > 0)
                {
                    TriggerEmotes(res.emotes);
                }
            }
            else
            {
                if (speechBubbleText != null)
                    speechBubbleText.text = "Connection error.";
                Debug.LogError(req.error);
            }
        }
    }

    private void TriggerEmotes(string[] emotes)
    {
        if (CharacterManager.Instance == null) return;

        var emoteAnimator = CharacterManager.Instance.GetEmoteAnimator();
        if (emoteAnimator == null) return;

        // Play the first emote (one at a time looks best)
        if (emotes.Length > 0)
        {
            Debug.Log($"Playing emote: {emotes[0]}");
            emoteAnimator.PlayEmote(emotes[0]);
        }
    }

    [System.Serializable]
    private class ChatRequest
    {
        public string message;
        public string character;
    }

    [System.Serializable]
    private class ChatResponse
    {
        public string reply;
        public string[] emotes;
    }

    [System.Serializable]
    private class CharacterInfo
    {
        public string name;
        public string tone;
    }
}
