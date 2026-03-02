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
    public GameObject sendButton;
    public GameObject switchButton;

    private string apiUrl = "http://127.0.0.1:5001/chat";
    private string streamUrl = "http://127.0.0.1:5001/chat/stream";

    // Text chat toggle
    private bool textChatVisible = false;
    private bool hasPendingResponse = false; // true when speech bubble has content to show

    // Speech bubble auto-dismiss
    private Coroutine _bubbleDismissTimer;
    private const float BubbleHoldTime  = 8f;  // seconds before fade starts
    private const float BubbleFadeTime  = 1.2f; // seconds the fade-out takes

    // Guards ResumeGesturesAfterEmote — true while a streaming request is in flight.
    // Set to false on the done event so we never restart the gesture loop after streaming ends.
    private bool _isStreaming = false;

    // --- Design tokens ---
    static readonly Color BgPanel     = new Color(0.07f, 0.07f, 0.11f, 0.88f); // near-black
    static readonly Color BgInput     = new Color(0.12f, 0.12f, 0.18f, 0.95f); // slightly lighter
    static readonly Color AccentColor = new Color(0.39f, 0.40f, 0.95f, 1.00f); // indigo
    static readonly Color AccentDim   = new Color(0.28f, 0.29f, 0.75f, 1.00f); // indigo pressed
    static readonly Color TextPrimary = new Color(0.95f, 0.95f, 1.00f, 1.00f); // near-white
    static readonly Color TextMuted   = new Color(0.50f, 0.52f, 0.65f, 1.00f); // muted blue-gray

    // Runtime refs to programmatic elements
    private GameObject chatPanel;      // bottom bar
    private GameObject toggleButton;   // 💬 button shown when chat is closed
    private Image micButtonImage;      // ref for colour pulsing when recording

    // Exposed for VoiceInputManager
    public Image  MicButtonImage    => micButtonImage;
    public Color  InputBgColor      => BgInput;

    void Start()
    {
        // Scale Canvas with screen
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800, 600);
            scaler.matchWidthOrHeight = 0.5f;
        }

        StyleSpeechBubble();
        BuildChatPanel(canvas);
        BuildToggleButton(canvas);

        UpdateCharacterNameUI();
        SetTextChatVisible(false);

        // Auto-attach VoiceInputManager if not already present
        if (GetComponent<VoiceInputManager>() == null)
            gameObject.AddComponent<VoiceInputManager>();
    }

    // ── Speech bubble ────────────────────────────────────────────────────────

    /// <summary>
    /// Show the speech bubble with the given text and restart the auto-dismiss timer.
    /// All code that wants to display something in the bubble should call this.
    /// </summary>
    private void ShowBubble(string text)
    {
        if (speechBubbleText != null) speechBubbleText.text = text;
        if (speechBubble == null) return;

        // Make sure it's fully opaque before showing
        var img = speechBubble.GetComponent<Image>();
        if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, 1f);
        if (speechBubbleText != null)
            speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, 1f);

        speechBubble.SetActive(true);

        // Restart dismiss timer
        if (_bubbleDismissTimer != null) StopCoroutine(_bubbleDismissTimer);
        _bubbleDismissTimer = StartCoroutine(DismissBubbleAfterDelay());
    }

    /// <summary>
    /// Hide the bubble immediately and cancel any pending dismiss timer.
    /// </summary>
    private void HideBubble()
    {
        if (_bubbleDismissTimer != null) { StopCoroutine(_bubbleDismissTimer); _bubbleDismissTimer = null; }
        if (speechBubble != null) speechBubble.SetActive(false);
    }

    /// <summary>
    /// Wait BubbleHoldTime seconds, then smoothly fade the bubble out.
    /// </summary>
    private IEnumerator DismissBubbleAfterDelay()
    {
        yield return new WaitForSeconds(BubbleHoldTime);

        if (speechBubble == null) yield break;

        var img  = speechBubble.GetComponent<Image>();
        float elapsed = 0f;
        while (elapsed < BubbleFadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / BubbleFadeTime);
            if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, alpha);
            if (speechBubbleText != null)
                speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            yield return null;
        }

        speechBubble.SetActive(false);
        // Restore full opacity so it looks right next time it appears
        if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, 1f);
        if (speechBubbleText != null)
            speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, 1f);
        _bubbleDismissTimer = null;
    }

    private void StyleSpeechBubble()
    {
        if (speechBubble == null) return;

        // Dark frosted-glass card
        var bg = speechBubble.GetComponent<Image>() ?? speechBubble.AddComponent<Image>();
        bg.color = BgPanel;

        // Anchor top-center, sits above the character
        var rect = speechBubble.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot     = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -20f);
            rect.sizeDelta = new Vector2(340f, 0f); // width fixed, height auto
        }

        // Layout group inside bubble
        var layout = speechBubble.GetComponent<VerticalLayoutGroup>() ?? speechBubble.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth  = true;
        layout.childControlHeight = true;

        // Bubble auto-height
        var fitter = speechBubble.GetComponent<ContentSizeFitter>() ?? speechBubble.AddComponent<ContentSizeFitter>();
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Text styling
        if (speechBubbleText != null)
        {
            speechBubbleText.color = TextPrimary;
            speechBubbleText.fontSize = 15f;
            speechBubbleText.enableWordWrapping = true;
            speechBubbleText.overflowMode = TextOverflowModes.Overflow;
            speechBubbleText.enableAutoSizing = false;
            speechBubbleText.alignment = TextAlignmentOptions.TopLeft;
            speechBubbleText.margin = Vector4.zero;

            var tf = speechBubbleText.GetComponent<ContentSizeFitter>() ?? speechBubbleText.gameObject.AddComponent<ContentSizeFitter>();
            tf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            tf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        speechBubble.SetActive(false);
    }

    // ── Bottom chat panel ────────────────────────────────────────────────────
    private void BuildChatPanel(Canvas canvas)
    {
        if (canvas == null) return;

        // Outer panel — full-width dark bar at bottom
        chatPanel = new GameObject("ChatPanel");
        chatPanel.transform.SetParent(canvas.transform, false);

        var panelRect = chatPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(1, 0);
        panelRect.pivot     = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(0, 64f);

        var panelBg = chatPanel.AddComponent<Image>();
        panelBg.color = BgPanel;

        var panelLayout = chatPanel.AddComponent<HorizontalLayoutGroup>();
        panelLayout.padding = new RectOffset(12, 8, 10, 10);
        panelLayout.spacing = 8f;
        panelLayout.childForceExpandWidth  = false;
        panelLayout.childForceExpandHeight = true;
        panelLayout.childControlWidth  = true;
        panelLayout.childControlHeight = true;
        panelLayout.childAlignment = TextAnchor.MiddleLeft;

        // ── Close / hide button (rightmost in panel) ──────────────────────
        GameObject closeBtn = new GameObject("CloseButton");
        closeBtn.transform.SetParent(chatPanel.transform, false);
        var closeBg = closeBtn.AddComponent<Image>();
        closeBg.color = BgInput;
        var closeButton = closeBtn.AddComponent<Button>();
        var closeCols = closeButton.colors;
        closeCols.normalColor      = BgInput;
        closeCols.highlightedColor = new Color(0.25f, 0.10f, 0.10f, 1f);
        closeCols.pressedColor     = new Color(0.40f, 0.10f, 0.10f, 1f);
        closeButton.colors = closeCols;
        closeButton.onClick.AddListener(OnToggleChat);
        var closeLE = closeBtn.AddComponent<LayoutElement>();
        closeLE.minWidth = 36f; closeLE.preferredWidth = 36f;
        closeLE.minHeight = 36f; closeLE.preferredHeight = 36f;
        closeLE.flexibleWidth = 0f;
        var closeIconObj = new GameObject("Icon");
        closeIconObj.transform.SetParent(closeBtn.transform, false);
        var closeIconRect = closeIconObj.AddComponent<RectTransform>();
        closeIconRect.anchorMin = Vector2.zero; closeIconRect.anchorMax = Vector2.one;
        closeIconRect.sizeDelta = Vector2.zero; closeIconRect.offsetMin = Vector2.zero; closeIconRect.offsetMax = Vector2.zero;
        var closeIcon = closeIconObj.AddComponent<TextMeshProUGUI>();
        closeIcon.text = "X"; closeIcon.fontSize = 13f;
        closeIcon.color = TextMuted; closeIcon.alignment = TextAlignmentOptions.Center;
        closeIcon.enableWordWrapping = false;
        closeBtn.transform.SetAsLastSibling(); // will be placed at end of layout

        // Reparent existing input field into panel
        if (userInputField != null)
        {
            userInputField.transform.SetParent(chatPanel.transform, false);

            var inputRect = userInputField.GetComponent<RectTransform>();
            var le = inputRect.GetComponent<LayoutElement>() ?? inputRect.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 40f;
            le.minHeight = 40f;

            var inputBg = userInputField.GetComponent<Image>();
            if (inputBg != null) inputBg.color = BgInput;

            userInputField.textComponent.color   = TextPrimary;
            userInputField.textComponent.fontSize = 15f;

            if (userInputField.placeholder != null)
            {
                var ph = userInputField.placeholder.GetComponent<TMP_Text>();
                if (ph != null)
                {
                    ph.text     = "Message...";
                    ph.fontSize = 15f;
                    ph.color    = TextMuted;
                }
            }
        }

        // Reparent Send button into panel
        if (sendButton != null)
        {
            sendButton.transform.SetParent(chatPanel.transform, false);

            var sendRect = sendButton.GetComponent<RectTransform>();
            var le = sendRect.GetComponent<LayoutElement>() ?? sendRect.gameObject.AddComponent<LayoutElement>();
            le.minWidth      = 72f;
            le.preferredWidth = 72f;
            le.preferredHeight = 40f;
            le.minHeight     = 40f;
            le.flexibleWidth = 0f;

            var sendBg = sendButton.GetComponent<Image>();
            if (sendBg != null) sendBg.color = AccentColor;

            var sendBtn = sendButton.GetComponent<Button>();
            if (sendBtn != null)
            {
                var cols = sendBtn.colors;
                cols.normalColor      = AccentColor;
                cols.highlightedColor = new Color(0.50f, 0.51f, 1.00f, 1f);
                cols.pressedColor     = AccentDim;
                sendBtn.colors = cols;
            }

            var sendLabel = sendButton.GetComponentInChildren<TMP_Text>();
            if (sendLabel != null)
            {
                sendLabel.text      = "Send";
                sendLabel.color     = Color.white;
                sendLabel.fontSize  = 14f;
                sendLabel.fontStyle = FontStyles.Bold;
                sendLabel.alignment = TextAlignmentOptions.Center;
            }
        }

        // ── Mic button (right of Send) ────────────────────────────────────
        {
            GameObject micBtn = new GameObject("MicButton");
            micBtn.transform.SetParent(chatPanel.transform, false);
            micButtonImage = micBtn.AddComponent<Image>();
            micButtonImage.color = BgInput;
            var micLE = micBtn.AddComponent<LayoutElement>();
            micLE.minWidth = 40f; micLE.preferredWidth = 40f;
            micLE.minHeight = 40f; micLE.preferredHeight = 40f;
            micLE.flexibleWidth = 0f;
            var micBtnComp = micBtn.AddComponent<Button>();
            var micCols = micBtnComp.colors;
            micCols.normalColor      = BgInput;
            micCols.highlightedColor = new Color(0.20f, 0.20f, 0.30f, 1f);
            micCols.pressedColor     = new Color(0.55f, 0.10f, 0.10f, 1f);
            micBtnComp.colors = micCols;
            micBtnComp.onClick.AddListener(() => {
                var vim = GetComponent<VoiceInputManager>();
                if (vim != null) vim.OnMicButtonPressed();
            });
            var micIconObj = new GameObject("Icon");
            micIconObj.transform.SetParent(micBtn.transform, false);
            var micIconRect = micIconObj.AddComponent<RectTransform>();
            micIconRect.anchorMin = Vector2.zero; micIconRect.anchorMax = Vector2.one;
            micIconRect.sizeDelta = Vector2.zero; micIconRect.offsetMin = Vector2.zero; micIconRect.offsetMax = Vector2.zero;
            var micIcon = micIconObj.AddComponent<TextMeshProUGUI>();
            micIcon.text = "MIC"; micIcon.fontSize = 11f;
            micIcon.alignment = TextAlignmentOptions.Center;
            micIcon.enableWordWrapping = false;

            // Ensure close button stays last
            closeBtn.transform.SetAsLastSibling();
        }

        // Reparent Switch button into panel (small, left of input)
        if (switchButton != null)
        {
            switchButton.transform.SetParent(chatPanel.transform, false);
            switchButton.transform.SetSiblingIndex(0); // before input field

            var swRect = switchButton.GetComponent<RectTransform>();
            var le = swRect.GetComponent<LayoutElement>() ?? swRect.gameObject.AddComponent<LayoutElement>();
            le.minWidth       = 36f;
            le.preferredWidth = 36f;
            le.preferredHeight = 36f;
            le.minHeight      = 36f;
            le.flexibleWidth  = 0f;

            var swBg = switchButton.GetComponent<Image>();
            if (swBg != null) swBg.color = BgInput;

            var swBtn = switchButton.GetComponent<Button>();
            if (swBtn != null)
            {
                var cols = swBtn.colors;
                cols.normalColor      = BgInput;
                cols.highlightedColor = new Color(0.20f, 0.20f, 0.30f, 1f);
                cols.pressedColor     = AccentDim;
                swBtn.colors = cols;
            }

            var swLabel = switchButton.GetComponentInChildren<TMP_Text>();
            if (swLabel != null)
            {
                swLabel.text      = "<>";
                swLabel.color     = TextMuted;
                swLabel.fontSize  = 18f;
                swLabel.alignment = TextAlignmentOptions.Center;
            }
        }

        // Style character name (top-left of panel, outside layout)
        if (characterNameText != null)
        {
            characterNameText.color    = TextMuted;
            characterNameText.fontSize = 11f;
            characterNameText.fontStyle = FontStyles.Bold;

            var nameRect = characterNameText.GetComponent<RectTransform>();
            if (nameRect != null)
            {
                nameRect.SetParent(chatPanel.transform, false);
                nameRect.anchorMin = new Vector2(0f, 1f);
                nameRect.anchorMax = new Vector2(0f, 1f);
                nameRect.pivot     = new Vector2(0f, 0f);
                nameRect.anchoredPosition = new Vector2(14f, 4f);
                nameRect.sizeDelta = new Vector2(160f, 16f);
            }
        }
    }

    // ── Toggle button ────────────────────────────────────────────────────────
    private void BuildToggleButton(Canvas canvas)
    {
        if (canvas == null) return;

        toggleButton = new GameObject("ChatToggleButton");
        toggleButton.transform.SetParent(canvas.transform, false);

        var rect = toggleButton.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot     = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-14f, 14f);
        rect.sizeDelta = new Vector2(46f, 46f);

        var bg = toggleButton.AddComponent<Image>();
        bg.color = AccentColor;

        var btn = toggleButton.AddComponent<Button>();
        var cols = btn.colors;
        cols.normalColor      = AccentColor;
        cols.highlightedColor = new Color(0.50f, 0.51f, 1.00f, 1f);
        cols.pressedColor     = AccentDim;
        btn.colors = cols;
        btn.onClick.AddListener(OnToggleChat);

        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(toggleButton.transform, false);
        var iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin  = Vector2.zero;
        iconRect.anchorMax  = Vector2.one;
        iconRect.sizeDelta  = Vector2.zero;
        iconRect.offsetMin  = Vector2.zero;
        iconRect.offsetMax  = Vector2.zero;

        var icon = iconObj.AddComponent<TextMeshProUGUI>();
        icon.text      = "Chat";
        icon.fontSize  = 13f;
        icon.fontStyle = FontStyles.Bold;
        icon.alignment = TextAlignmentOptions.Center;
        icon.enableWordWrapping = false;
    }

    /// <summary>
    /// Called by the chat toggle button.
    /// </summary>
    public void OnToggleChat()
    {
        SetTextChatVisible(!textChatVisible);
    }

    public void SetTextChatVisible(bool visible)
    {
        textChatVisible = visible;

        // Show/hide the whole bottom chat panel
        if (chatPanel != null)
            chatPanel.SetActive(visible);

        // 💬 button always stays visible as a fallback
        if (toggleButton != null)
            toggleButton.SetActive(!visible);

        // Speech bubble is an independent HUD element — don't touch it here

        // Focus input when opening
        if (visible && userInputField != null)
        {
            userInputField.ActivateInputField();
            userInputField.Select();
        }
    }

    /// <summary>
    /// Show a temporary status message in the speech bubble (used by VoiceInputManager).
    /// </summary>
    public void SetStatusText(string text)
    {
        if (string.IsNullOrEmpty(text))
            HideBubble();
        else
            ShowBubble(text);
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
            hasPendingResponse = false;
            HideBubble(); // cancels _bubbleDismissTimer + hides bubble

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
        // Show "..." immediately — give visual feedback while waiting for the first token.
        // Cancel any running dismiss timer but do NOT start a new one; the bubble must stay
        // until the response arrives (which can take several seconds on local Ollama).
        if (speechBubbleText != null) speechBubbleText.text = "...";
        hasPendingResponse = true;
        if (speechBubble != null)
        {
            var loadImg = speechBubble.GetComponent<Image>();
            if (loadImg != null) loadImg.color = new Color(loadImg.color.r, loadImg.color.g, loadImg.color.b, 1f);
            if (speechBubbleText != null)
                speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, 1f);
            if (_bubbleDismissTimer != null) { StopCoroutine(_bubbleDismissTimer); _bubbleDismissTimer = null; }
            speechBubble.SetActive(true);
        }

        // Send current character so backend uses the right personality
        string charName = CharacterManager.Instance != null
            ? CharacterManager.Instance.GetCurrentCharacterName()
            : "female_default";
        string jsonBody = JsonUtility.ToJson(new ChatRequest { message = message, character = charName });
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);

        // Get animators once
        var faceAnim = CharacterManager.Instance?.GetFaceAnimator();
        var emoteAnimator = CharacterManager.Instance?.GetEmoteAnimator();

        // Use streaming endpoint — tokens arrive one at a time via SSE
        using (UnityWebRequest req = new UnityWebRequest(streamUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            _isStreaming = true;
            req.SendWebRequest();

            string fullRawText = "";
            int lastProcessed = 0;
            int detectedEmoteCount = 0; // Track in-stream emotes

            // Start mouth animation + body gestures while tokens stream in
            if (faceAnim != null)
                faceAnim.StartTalking();
            if (emoteAnimator != null)
                emoteAnimator.StartTalkingGestures();

            // Poll for incoming data while the request is in progress.
            // IMPORTANT: check isDone AFTER reading data, not before.
            // A short first response can arrive in the same frame isDone becomes true;
            // the old `while (!req.isDone)` would exit before ever reading that data.
            while (true)
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
                                // Use a specific key:value check to avoid false-positives
                                // when a token happens to contain the word "done" quoted.
                                bool isDoneEvent = jsonStr.Contains("\"done\":true")
                                                || jsonStr.Contains("\"done\": true");
                                // Check if this is a token or the final message
                                if (isDoneEvent)
                                {
                                    // Final message with emotes + emotion — parse it
                                    StreamDone done = JsonUtility.FromJson<StreamDone>(jsonStr);

                                    // Stop talking gestures — transition to emote animations
                                    if (emoteAnimator != null)
                                        emoteAnimator.StopTalkingGestures();

                                    if (done.emotes != null && done.emotes.Length > 0)
                                    {
                                        TriggerEmotes(done.emotes);
                                    }
                                    // Set facial expression + guaranteed body animation
                                    if (!string.IsNullOrEmpty(done.emotion))
                                    {
                                        TriggerEmotion(done.emotion);
                                    }
                                    // Set clean reply — strip any emotes the server may have
                                    // missed (belt-and-suspenders safety net)
                                    if (!string.IsNullOrEmpty(done.reply))
                                    {
                                        string safeReply = System.Text.RegularExpressions.Regex
                                            .Replace(done.reply, @"\*[^*]+\*", "");
                                        safeReply = System.Text.RegularExpressions.Regex
                                            .Replace(safeReply, @"\s{2,}", " ").Trim();
                                        ShowBubble(safeReply);
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

                                        // Feed accumulated text to gesture system for context awareness
                                        if (emoteAnimator != null)
                                            emoteAnimator.UpdateStreamText(fullRawText);

                                        // === In-stream emote detection ===
                                        // Detect *emote* markers as they complete and trigger immediately
                                        var allEmotes = System.Text.RegularExpressions.Regex.Matches(
                                            fullRawText, @"\*([^*]+)\*");
                                        if (allEmotes.Count > detectedEmoteCount)
                                        {
                                            // New emote just completed! Trigger it now
                                            string newEmote = allEmotes[allEmotes.Count - 1].Groups[1].Value;
                                            Debug.Log($"In-stream emote detected: *{newEmote}*");
                                            if (emoteAnimator != null)
                                            {
                                                // Stop gestures, play the specific emote
                                                emoteAnimator.StopTalkingGestures();
                                                emoteAnimator.PlayEmote(newEmote);
                                                // Resume gestures after a delay
                                                StartCoroutine(ResumeGesturesAfterEmote(emoteAnimator, 2.5f));
                                            }
                                            // Also set face from the emote
                                            if (faceAnim != null)
                                                faceAnim.SetEmotionFromEmote(newEmote);
                                            detectedEmoteCount = allEmotes.Count;
                                        }

                                        // Strip complete emote markers *like this*
                                        string display = System.Text.RegularExpressions.Regex.Replace(
                                            fullRawText, @"\*[^*]+\*", "");
                                        // Also hide any incomplete emote still being typed *like thi
                                        display = System.Text.RegularExpressions.Regex.Replace(
                                            display, @"\*[^*]*$", "");
                                        display = System.Text.RegularExpressions.Regex.Replace(
                                            display, @"\s{2,}", " ").Trim();
                                        if (speechBubbleText != null)
                                            ShowBubble(display);
                                    }
                                }
                            }
                            catch (System.Exception) { /* skip malformed JSON */ }
                        }
                    }
                }
                // Check isDone AFTER processing data — ensures we never skip
                // a final chunk that arrived in the same frame as the done signal.
                if (req.isDone) break;
                yield return null; // Wait one frame
            }

            // Handle any errors
            if (req.result != UnityWebRequest.Result.Success)
            {
                _isStreaming = false;
                ShowBubble("Connection error."); // auto-dismisses after BubbleHoldTime
                Debug.LogError(req.error);
                if (faceAnim != null)
                    faceAnim.StopTalking();
                if (emoteAnimator != null)
                    emoteAnimator.StopTalkingGestures();
            }
        }
    }

    /// <summary>
    /// Resume talking gestures after an in-stream emote finishes.
    /// </summary>
    private IEnumerator ResumeGesturesAfterEmote(EmoteAnimator emoteAnimator, float maxWait)
    {
        // Wait for the emote to finish
        float elapsed = 0f;
        while (emoteAnimator.IsPlaying && elapsed < maxWait)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        // Only resume gestures if the stream is still active.
        // If the done event fired while this emote was playing, _isStreaming is already false
        // and StopTalkingGestures was already called — calling Start again would loop forever.
        if (_isStreaming)
            emoteAnimator.StartTalkingGestures();
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

        // ALSO trigger body animation from emotion as a GUARANTEED fallback.
        // Uses a wait-until-ready loop instead of a simple delay — ensures it always fires.
        var emoteAnimator = CharacterManager.Instance.GetEmoteAnimator();
        if (emoteAnimator != null)
        {
            StartCoroutine(WaitAndPlayEmotionAnimation(emoteAnimator, emotion, 5f));
        }
    }

    /// <summary>
    /// Wait until any current emote finishes, then trigger an emotion body animation.
    /// Retries for up to maxWait seconds — guarantees the animation eventually plays.
    /// </summary>
    private IEnumerator WaitAndPlayEmotionAnimation(EmoteAnimator emoteAnimator, string emotion, float maxWait)
    {
        float elapsed = 0f;
        // Wait until the emote animator is free
        while (emoteAnimator.IsPlaying && elapsed < maxWait)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        Debug.Log($"Emotion body animation firing: {emotion} (waited {elapsed:F1}s)");
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
