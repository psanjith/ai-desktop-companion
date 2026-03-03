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

    private string streamUrl = "http://127.0.0.1:5001/chat/stream";
    private string clearUrl  = "http://127.0.0.1:5001/memory/clear";
    private const string SpeakUrl = "http://127.0.0.1:5001/speak";

    // Text chat toggle
    private bool textChatVisible = false;

    // Speech bubble auto-dismiss
    private Coroutine _bubbleDismissTimer;
    private const float BubbleHoldTime  = 8f;  // seconds before fade starts
    private const float BubbleFadeTime  = 1.2f; // seconds the fade-out takes

    // Guards ResumeGesturesAfterEmote — true while a streaming request is in flight.
    // Set to false on the done event so we never restart the gesture loop after streaming ends.
    private bool _isStreaming  = false;
    private Coroutine _dotCoroutine; // animated loading dots while waiting for first token

    // --- Design tokens ---
    static readonly Color BgPanel     = new Color(0.05f, 0.06f, 0.11f, 0.82f); // deep navy, translucent
    static readonly Color BgInput     = new Color(0.10f, 0.11f, 0.17f, 1.00f); // input well
    static readonly Color BgBubble    = new Color(0.06f, 0.07f, 0.12f, 0.90f); // speech bubble bg
    static readonly Color BgButton    = new Color(0.13f, 0.14f, 0.21f, 0.90f); // subtle button bg
    static readonly Color AccentColor = new Color(0.33f, 0.46f, 0.85f, 1.00f); // cool blue, refined
    static readonly Color AccentHover = new Color(0.43f, 0.56f, 0.92f, 1.00f); // lighter on hover
    static readonly Color AccentDim   = new Color(0.24f, 0.35f, 0.70f, 1.00f); // pressed
    static readonly Color TextPrimary = new Color(0.92f, 0.93f, 0.98f, 1.00f); // near-white
    static readonly Color TextMuted   = new Color(0.40f, 0.43f, 0.56f, 1.00f); // muted

    // Runtime refs to programmatic elements
    private GameObject chatPanel;      // bottom bar
    private GameObject toggleButton;   // button shown when chat is closed
    private Image micButtonImage;      // ref for colour pulsing when recording
    private TMP_Text micLabel;         // ref for "MIC" / "REC" label updates
    private Button _sendBtn;           // ref to lock/unlock during streaming
    private Image  _statusDot;         // green/red connection indicator
    private const string HealthUrl = "http://127.0.0.1:5001/character";

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
        BuildStatusDot(canvas);

        UpdateCharacterNameUI();
        SetTextChatVisible(false);

        // Auto-attach VoiceInputManager if not already present
        if (GetComponent<VoiceInputManager>() == null)
            gameObject.AddComponent<VoiceInputManager>();

        // Auto-attach WindowDragger for click-drag window repositioning
        if (GetComponent<WindowDragger>() == null)
            gameObject.AddComponent<WindowDragger>();

        // Start periodic backend health-check
        StartCoroutine(HealthCheckLoop());
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
    /// Update bubble text in-place — does NOT restart the dismiss timer.
    /// Use during live token streaming to avoid resetting the auto-dismiss countdown
    /// on every incoming token.
    /// </summary>
    private void SetBubbleText(string text)
    {
        if (speechBubbleText == null) return;
        speechBubbleText.text  = text;
        speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, 1f);
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

    // ── Helper: apply Unity's built-in rounded-rect sprite for soft edges ────
    static void MakeRounded(Image img)
    {
        img.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        img.type   = Image.Type.Sliced;
    }

    private void StyleSpeechBubble()
    {
        if (speechBubble == null) return;

        var bg = speechBubble.GetComponent<Image>() ?? speechBubble.AddComponent<Image>();
        bg.color = BgBubble;
        MakeRounded(bg);

        var bubbleShadow = speechBubble.GetComponent<Shadow>() ?? speechBubble.AddComponent<Shadow>();
        bubbleShadow.effectColor    = new Color(0f, 0f, 0f, 0.35f);
        bubbleShadow.effectDistance = new Vector2(0f, -3f);

        // Anchor top-center, floats above the character
        var rect = speechBubble.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot     = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -16f);
            rect.sizeDelta = new Vector2(300f, 0f);
        }

        var layout = speechBubble.GetComponent<VerticalLayoutGroup>() ?? speechBubble.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 10);
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth  = true;
        layout.childControlHeight = true;

        var fitter = speechBubble.GetComponent<ContentSizeFitter>() ?? speechBubble.AddComponent<ContentSizeFitter>();
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        if (speechBubbleText != null)
        {
            speechBubbleText.color = TextPrimary;
            speechBubbleText.fontSize = 14f;
            speechBubbleText.lineSpacing = 4f;
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
        panelRect.offsetMin = new Vector2(8f, 8f);
        panelRect.offsetMax = new Vector2(-8f, 64f); // 56px tall, floating 8px from edges

        var panelBg = chatPanel.AddComponent<Image>();
        panelBg.color = BgPanel;
        MakeRounded(panelBg);

        var panelShadow = chatPanel.AddComponent<Shadow>();
        panelShadow.effectColor    = new Color(0f, 0f, 0f, 0.40f);
        panelShadow.effectDistance = new Vector2(0f, -4f);

        var panelLayout = chatPanel.AddComponent<HorizontalLayoutGroup>();
        panelLayout.padding = new RectOffset(10, 8, 8, 8);
        panelLayout.spacing = 6f;
        panelLayout.childForceExpandWidth  = false;
        panelLayout.childForceExpandHeight = true;
        panelLayout.childControlWidth  = true;
        panelLayout.childControlHeight = true;
        panelLayout.childAlignment = TextAnchor.MiddleLeft;

        // ── Close / hide button (rightmost in panel) ──────────────────────
        GameObject closeBtn = new GameObject("CloseButton");
        closeBtn.transform.SetParent(chatPanel.transform, false);
        var closeBg = closeBtn.AddComponent<Image>();
        closeBg.color = BgButton;
        MakeRounded(closeBg);
        var closeButton = closeBtn.AddComponent<Button>();
        var closeCols = closeButton.colors;
        closeCols.normalColor      = BgButton;
        closeCols.highlightedColor = new Color(0.20f, 0.22f, 0.32f, 1f);
        closeCols.pressedColor     = new Color(0.27f, 0.28f, 0.38f, 1f);
        closeCols.fadeDuration     = 0.12f;
        closeButton.colors = closeCols;
        closeButton.onClick.AddListener(OnToggleChat);
        var closeLE = closeBtn.AddComponent<LayoutElement>();
        closeLE.minWidth = 32f; closeLE.preferredWidth = 32f;
        closeLE.minHeight = 32f; closeLE.preferredHeight = 32f;
        closeLE.flexibleWidth = 0f;
        var closeIconObj = new GameObject("Icon");
        closeIconObj.transform.SetParent(closeBtn.transform, false);
        var closeIconRect = closeIconObj.AddComponent<RectTransform>();
        closeIconRect.anchorMin = Vector2.zero; closeIconRect.anchorMax = Vector2.one;
        closeIconRect.sizeDelta = Vector2.zero; closeIconRect.offsetMin = Vector2.zero; closeIconRect.offsetMax = Vector2.zero;
        var closeIcon = closeIconObj.AddComponent<TextMeshProUGUI>();
        closeIcon.text = "x"; closeIcon.fontSize = 13f;
        closeIcon.color = TextMuted;
        closeIcon.alignment = TextAlignmentOptions.Center;
        closeIcon.enableWordWrapping = false;
        closeBtn.transform.SetAsLastSibling(); // will be placed at end of layout

        // Reparent existing input field into panel
        if (userInputField != null)
        {
            userInputField.transform.SetParent(chatPanel.transform, false);

            var inputRect = userInputField.GetComponent<RectTransform>();
            var le = inputRect.GetComponent<LayoutElement>() ?? inputRect.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 38f;
            le.minHeight = 38f;

            var inputBg = userInputField.GetComponent<Image>();
            if (inputBg != null) { inputBg.color = BgInput; MakeRounded(inputBg); }

            userInputField.textComponent.color   = TextPrimary;
            userInputField.textComponent.fontSize = 14f;

            if (userInputField.placeholder != null)
            {
                var ph = userInputField.placeholder.GetComponent<TMP_Text>();
                if (ph != null)
                {
                    ph.text     = "Say something...";
                    ph.fontSize = 14f;
                    ph.color    = TextMuted;
                    ph.fontStyle = FontStyles.Italic;
                }
            }
            // Enter key submits the message
            userInputField.onSubmit.AddListener(_ => OnSendMessage());
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
            if (sendBg != null) { sendBg.color = AccentColor; MakeRounded(sendBg); }

            var sendBtn = sendButton.GetComponent<Button>();
            if (sendBtn != null)
            {
                _sendBtn = sendBtn;
                var cols = sendBtn.colors;
                cols.normalColor      = AccentColor;
                cols.highlightedColor = AccentHover;
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
            micButtonImage.color = BgButton;
            MakeRounded(micButtonImage);
            var micLE = micBtn.AddComponent<LayoutElement>();
            micLE.minWidth = 38f; micLE.preferredWidth = 38f;
            micLE.minHeight = 38f; micLE.preferredHeight = 38f;
            micLE.flexibleWidth = 0f;
            var micBtnComp = micBtn.AddComponent<Button>();
            var micCols = micBtnComp.colors;
            micCols.normalColor      = BgButton;
            micCols.highlightedColor = new Color(0.20f, 0.22f, 0.32f, 1f);
            micCols.pressedColor     = new Color(0.27f, 0.28f, 0.38f, 1f);
            micCols.fadeDuration     = 0.12f;
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
            micIcon.color = TextMuted;
            micIcon.alignment = TextAlignmentOptions.Center;
            micIcon.enableWordWrapping = false;
            micLabel = micIcon; // saved for dynamic label updates

            // ── Clear memory button ───────────────────────────────────────
            GameObject clrBtn = new GameObject("ClearButton");
            clrBtn.transform.SetParent(chatPanel.transform, false);
            var clrBg = clrBtn.AddComponent<Image>();
            clrBg.color = new Color(0.18f, 0.10f, 0.10f, 0.90f);
            MakeRounded(clrBg);
            var clrLE = clrBtn.AddComponent<LayoutElement>();
            clrLE.minWidth = 38f; clrLE.preferredWidth = 38f;
            clrLE.minHeight = 38f; clrLE.preferredHeight = 38f;
            clrLE.flexibleWidth = 0f;
            var clrBtnComp = clrBtn.AddComponent<Button>();
            var clrCols = clrBtnComp.colors;
            clrCols.normalColor      = new Color(0.18f, 0.10f, 0.10f, 0.90f);
            clrCols.highlightedColor = new Color(0.28f, 0.14f, 0.14f, 1f);
            clrCols.pressedColor     = new Color(0.38f, 0.18f, 0.18f, 1f);
            clrCols.fadeDuration     = 0.12f;
            clrBtnComp.colors = clrCols;
            clrBtnComp.onClick.AddListener(() => StartCoroutine(ClearMemory()));
            var clrIconObj = new GameObject("Icon");
            clrIconObj.transform.SetParent(clrBtn.transform, false);
            var clrIconRect = clrIconObj.AddComponent<RectTransform>();
            clrIconRect.anchorMin = Vector2.zero; clrIconRect.anchorMax = Vector2.one;
            clrIconRect.sizeDelta = Vector2.zero; clrIconRect.offsetMin = Vector2.zero; clrIconRect.offsetMax = Vector2.zero;
            var clrIcon = clrIconObj.AddComponent<TextMeshProUGUI>();
            clrIcon.text = "CLR"; clrIcon.fontSize = 10f;
            clrIcon.color = new Color(0.70f, 0.35f, 0.35f, 1f);
            clrIcon.alignment = TextAlignmentOptions.Center;
            clrIcon.enableWordWrapping = false;

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
            le.minWidth       = 38f;
            le.preferredWidth = 38f;
            le.preferredHeight = 38f;
            le.minHeight      = 38f;
            le.flexibleWidth  = 0f;

            var swBg = switchButton.GetComponent<Image>();
            if (swBg != null) { swBg.color = BgButton; MakeRounded(swBg); }

            var swBtn = switchButton.GetComponent<Button>();
            if (swBtn != null)
            {
                var cols = swBtn.colors;
                cols.normalColor      = BgButton;
                cols.highlightedColor = new Color(0.20f, 0.22f, 0.32f, 1f);
                cols.pressedColor     = new Color(0.27f, 0.28f, 0.38f, 1f);
                cols.fadeDuration     = 0.12f;
                swBtn.colors = cols;
                swBtn.onClick.AddListener(OnSwitchCharacter);
            }

            var swLabel = switchButton.GetComponentInChildren<TMP_Text>();
            if (swLabel != null)
            {
                swLabel.text      = ">>";
                swLabel.color     = TextMuted;
                swLabel.fontSize  = 12f;
                swLabel.fontStyle = FontStyles.Bold;
                swLabel.alignment = TextAlignmentOptions.Center;
            }
        }

        // Style character name (top-left of panel, outside layout)
        if (characterNameText != null)
        {
            characterNameText.color     = new Color(0.55f, 0.65f, 0.95f, 0.80f);
            characterNameText.fontSize  = 10f;
            characterNameText.fontStyle = FontStyles.Bold;

            var nameRect = characterNameText.GetComponent<RectTransform>();
            if (nameRect != null)
            {
                nameRect.SetParent(chatPanel.transform, false);
                nameRect.anchorMin = new Vector2(0f, 1f);
                nameRect.anchorMax = new Vector2(0f, 1f);
                nameRect.pivot     = new Vector2(0f, 0f);
                nameRect.anchoredPosition = new Vector2(12f, 3f);
                nameRect.sizeDelta = new Vector2(140f, 14f);
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
        rect.anchoredPosition = new Vector2(-12f, 12f);
        rect.sizeDelta = new Vector2(52f, 36f);

        var bg = toggleButton.AddComponent<Image>();
        bg.color = AccentColor;
        MakeRounded(bg);

        var btn = toggleButton.AddComponent<Button>();
        var cols = btn.colors;
        cols.normalColor      = AccentColor;
        cols.highlightedColor = AccentHover;
        cols.pressedColor     = AccentDim;
        cols.fadeDuration     = 0.08f;
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
        icon.fontSize  = 12f;
        icon.fontStyle = FontStyles.Bold;
        icon.alignment = TextAlignmentOptions.Center;
        icon.enableWordWrapping = false;
    }

    // ── Status dot ────────────────────────────────────────────────────────────
    /// <summary>
    /// A small circle in the bottom-right corner: green = backend reachable, red = offline.
    /// </summary>
    private void BuildStatusDot(Canvas canvas)
    {
        if (canvas == null) return;

        var dotObj = new GameObject("StatusDot");
        dotObj.transform.SetParent(canvas.transform, false);

        var rect = dotObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot     = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-10f, 10f);
        rect.sizeDelta = new Vector2(8f, 8f);

        _statusDot = dotObj.AddComponent<Image>();
        _statusDot.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
        _statusDot.color  = new Color(0.3f, 0.7f, 0.4f, 0.85f); // green — optimistic default
    }

    private IEnumerator HealthCheckLoop()
    {
        while (true)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(HealthUrl))
            {
                req.timeout = 2;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                if (_statusDot != null)
                    _statusDot.color = ok
                        ? new Color(0.3f, 0.7f, 0.4f, 0.85f)   // green
                        : new Color(0.85f, 0.3f, 0.3f, 0.85f);  // red
            }
            yield return new WaitForSeconds(5f);
        }
    }

    /// <summary>
    /// Per-frame keyboard shortcuts — only fire when the text input field is not focused.
    /// </summary>
    void Update()
    {
        if (userInputField != null && userInputField.isFocused) return;

        // M = toggle microphone
        if (Input.GetKeyDown(KeyCode.M))
        {
            var vim = GetComponent<VoiceInputManager>();
            if (vim != null) vim.OnMicButtonPressed();
        }
        // C = toggle chat panel open/closed
        if (Input.GetKeyDown(KeyCode.C))
            OnToggleChat();
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

        // Chat toggle button always stays visible as a fallback
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
    /// Updates the mic button label — called by VoiceInputManager when recording starts/stops.
    /// </summary>
    public void SetMicActiveLabel(bool recording)
    {
        if (micLabel == null) return;
        micLabel.text  = recording ? "REC" : "MIC";
        micLabel.color = recording
            ? new Color(0.95f, 0.35f, 0.35f, 1f)  // red while recording
            : TextMuted;
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
        if (_isStreaming) return; // ignore while a response is in flight
        string message = userInputField.text.Trim();
        if (string.IsNullOrEmpty(message)) return;
        userInputField.text = "";
        StartCoroutine(SendToAI(message));
    }

    IEnumerator SendToAI(string message)
    {
        // Show animated loading dots while waiting for the first token.
        // Cancel any running dismiss timer but do NOT start a new one; the bubble must stay
        // until the response arrives (which can take several seconds on local Ollama).
        if (speechBubble != null)
        {
            var loadImg = speechBubble.GetComponent<Image>();
            if (loadImg != null) loadImg.color = new Color(loadImg.color.r, loadImg.color.g, loadImg.color.b, 1f);
            if (speechBubbleText != null)
                speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, 1f);
            if (_bubbleDismissTimer != null) { StopCoroutine(_bubbleDismissTimer); _bubbleDismissTimer = null; }
            speechBubble.SetActive(true);
        }
        if (_dotCoroutine != null) StopCoroutine(_dotCoroutine);
        _dotCoroutine = StartCoroutine(AnimateThinkingDots());

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
            SetInputLocked(true);
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
                                        if (_dotCoroutine != null) { StopCoroutine(_dotCoroutine); _dotCoroutine = null; }
                                        ShowBubble(safeReply);
                                        StartCoroutine(SpeakReply(safeReply, charName));
                                    }
                                    // Stop talking mouth
                                    if (faceAnim != null)
                                        faceAnim.StopTalking();
                                    _isStreaming = false;
                                    SetInputLocked(false);
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
                                        if (display.Length > 0)
                                        {
                                            if (_dotCoroutine != null) { StopCoroutine(_dotCoroutine); _dotCoroutine = null; }
                                            SetBubbleText(display);
                                        }
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
                SetInputLocked(false);
                if (_dotCoroutine != null) { StopCoroutine(_dotCoroutine); _dotCoroutine = null; }
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

    private void SetInputLocked(bool locked)
    {
        if (userInputField != null) userInputField.interactable = !locked;
        if (_sendBtn != null)
        {
            _sendBtn.interactable = !locked;
            var img = _sendBtn.GetComponent<Image>();
            if (img != null)
                img.color = locked
                    ? new Color(AccentDim.r, AccentDim.g, AccentDim.b, 0.5f)  // dimmed while waiting
                    : AccentColor;
        }
    }

    private IEnumerator AnimateThinkingDots()
    {
        string[] frames = { ".  ", ".. ", "..." };
        int i = 0;
        while (true)
        {
            if (speechBubbleText != null) speechBubbleText.text = frames[i % 3];
            i++;
            yield return new WaitForSeconds(0.4f);
        }
    }

    /// <summary>
    /// Fire-and-forget coroutine: sends cleaned reply text to /speak for macOS TTS.
    /// </summary>
    IEnumerator SpeakReply(string text, string character)
    {
        string body = JsonUtility.ToJson(new SpeakRequest { text = text, character = character });
        byte[] raw  = System.Text.Encoding.UTF8.GetBytes(body);
        using (UnityWebRequest req = new UnityWebRequest(SpeakUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
        }
    }

    IEnumerator ClearMemory()
    {
        string charName = CharacterManager.Instance != null
            ? CharacterManager.Instance.GetCurrentCharacterName()
            : "female_default";
        string jsonBody = JsonUtility.ToJson(new ClearRequest { character = charName });
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        using (UnityWebRequest req = new UnityWebRequest(clearUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
        }
        ShowBubble("Memory cleared.");
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
    private class ClearRequest
    {
        public string character;
    }

    [System.Serializable]
    private class SpeakRequest
    {
        public string text;
        public string character;
    }

    [System.Serializable]
    private class CharacterInfo
    {
        public string name;
        public string tone;
    }
}
