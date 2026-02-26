using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using System.Text;

public class CompanionController : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField userInputField;
    public TMP_Text speechBubbleText;
    public GameObject speechBubble;
    public TMP_Text characterNameText; // Optional: shows current character name

    private string apiUrl = "http://127.0.0.1:5001/chat";
    private string streamUrl = "http://127.0.0.1:5001/chat/stream";

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

        // Set up text to fit properly in the bubble
        if (speechBubbleText != null)
        {
            speechBubbleText.enableWordWrapping = true;
            speechBubbleText.overflowMode = TextOverflowModes.Overflow;
            speechBubbleText.enableAutoSizing = true;
            speechBubbleText.fontSizeMin = 10;
            speechBubbleText.fontSizeMax = 20;
            speechBubbleText.alignment = TextAlignmentOptions.TopLeft;
            speechBubbleText.margin = new Vector4(8, 5, 8, 5);

            // Make the bubble grow vertically to fit longer replies
            var textFitter = speechBubbleText.GetComponent<ContentSizeFitter>();
            if (textFitter == null)
                textFitter = speechBubbleText.gameObject.AddComponent<ContentSizeFitter>();
            textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            textFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        // Make the speech bubble itself grow to match its text content
        if (speechBubble != null)
        {
            var bubbleFitter = speechBubble.GetComponent<ContentSizeFitter>();
            if (bubbleFitter == null)
                bubbleFitter = speechBubble.AddComponent<ContentSizeFitter>();
            bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Add a VerticalLayoutGroup so the bubble wraps around its children
            var layout = speechBubble.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
                layout = speechBubble.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
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
            speechBubbleText.text = "";
        if (speechBubble != null)
            speechBubble.SetActive(true);

        // Send current character so backend uses the right personality
        string charName = CharacterManager.Instance != null
            ? CharacterManager.Instance.GetCurrentCharacterName()
            : "female_default";
        string jsonBody = JsonUtility.ToJson(new ChatRequest { message = message, character = charName });
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

        // Use streaming endpoint — tokens arrive one at a time via SSE
        using (UnityWebRequest req = new UnityWebRequest(streamUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            req.SendWebRequest();

            string fullRawText = "";
            int lastProcessed = 0;

            // Start mouth animation while tokens stream in
            var faceAnim = CharacterManager.Instance?.GetFaceAnimator();
            if (faceAnim != null)
                faceAnim.StartTalking();

            // Poll for incoming data while the request is in progress
            while (!req.isDone)
            {
                string currentData = req.downloadHandler?.text ?? "";
                if (currentData.Length > lastProcessed)
                {
                    // Process new SSE data lines
                    string newData = currentData.Substring(lastProcessed);
                    lastProcessed = currentData.Length;

                    // Parse SSE lines: "data: {...}\n\n"
                    string[] lines = newData.Split('\n');
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("data: "))
                        {
                            string jsonStr = trimmed.Substring(6);
                            try
                            {
                                // Check if this is a token or the final message
                                if (jsonStr.Contains("\"done\""))
                                {
                                    // Final message with emotes + emotion — parse it
                                    StreamDone done = JsonUtility.FromJson<StreamDone>(jsonStr);
                                    if (done.emotes != null && done.emotes.Length > 0)
                                    {
                                        TriggerEmotes(done.emotes);
                                    }
                                    // Set facial expression from detected emotion
                                    if (!string.IsNullOrEmpty(done.emotion))
                                    {
                                        TriggerEmotion(done.emotion);
                                    }
                                    // Set clean reply (without emote markers)
                                    if (!string.IsNullOrEmpty(done.reply))
                                    {
                                        speechBubbleText.text = done.reply;
                                    }
                                    // Stop talking mouth
                                    if (faceAnim != null)
                                        faceAnim.StopTalking();
                                }
                                else
                                {
                                    // It's a token chunk — append to display
                                    StreamToken tok = JsonUtility.FromJson<StreamToken>(jsonStr);
                                    if (tok.token != null)
                                    {
                                        fullRawText += tok.token;
                                        // Strip complete emote markers *like this*
                                        string display = System.Text.RegularExpressions.Regex.Replace(
                                            fullRawText, @"\*[^*]+\*", "");
                                        // Also hide any incomplete emote still being typed *like thi
                                        display = System.Text.RegularExpressions.Regex.Replace(
                                            display, @"\*[^*]*$", "");
                                        display = System.Text.RegularExpressions.Regex.Replace(
                                            display, @"\s{2,}", " ").Trim();
                                        if (speechBubbleText != null)
                                            speechBubbleText.text = display;
                                    }
                                }
                            }
                            catch (System.Exception) { /* skip malformed JSON */ }
                        }
                    }
                }
                yield return null; // Wait one frame
            }

            // Handle any errors
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (speechBubbleText != null)
                    speechBubbleText.text = "Connection error.";
                Debug.LogError(req.error);
                if (faceAnim != null)
                    faceAnim.StopTalking();
            }
        }
    }

    private void TriggerEmotes(string[] emotes)
    {
        if (CharacterManager.Instance == null) return;

        var emoteAnimator = CharacterManager.Instance.GetEmoteAnimator();
        if (emoteAnimator == null) return;

        if (emotes.Length > 0)
        {
            // Combine ALL emotes into one string for better keyword matching
            string combined = string.Join(" ", emotes);
            Debug.Log($"Playing emote (combined): {combined}");
            emoteAnimator.PlayEmote(combined);
        }
    }

    private void TriggerEmotion(string emotion)
    {
        if (CharacterManager.Instance == null) return;

        // Always set facial expression
        var faceAnimator = CharacterManager.Instance.GetFaceAnimator();
        if (faceAnimator != null)
        {
            Debug.Log($"Setting emotion: {emotion}");
            faceAnimator.SetEmotion(emotion);
        }

        // ALSO trigger body animation from emotion as a guaranteed fallback.
        // This fires after a short delay so the emote animation can play first.
        // If the emote is still playing, PlayEmotionAnimation will skip (isPlaying check).
        var emoteAnimator = CharacterManager.Instance.GetEmoteAnimator();
        if (emoteAnimator != null)
        {
            StartCoroutine(DelayedEmotionAnimation(emoteAnimator, emotion, 1.5f));
        }
    }

    /// <summary>
    /// Wait for any current emote to finish, then trigger an emotion-based body animation.
    /// Guarantees visible bone movement on every single response.
    /// </summary>
    private IEnumerator DelayedEmotionAnimation(EmoteAnimator emoteAnimator, string emotion, float delay)
    {
        yield return new WaitForSeconds(delay);
        // Only play if the previous emote animation has finished
        emoteAnimator.PlayEmotionAnimation(emotion);
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
    private class StreamToken
    {
        public string token;
    }

    [System.Serializable]
    private class StreamDone
    {
        public bool done;
        public string reply;
        public string[] emotes;
        public string emotion;
    }

    [System.Serializable]
    private class CharacterInfo
    {
        public string name;
        public string tone;
    }
}
