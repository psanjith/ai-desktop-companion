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

    [Header("Backend")]
    [SerializeField] private string backendBaseUrl = "http://127.0.0.1:5001";

    private string streamUrl;
    private string clearUrl;
    private string speakUrl;
    private string desktopContextUrl;
    private string healthUrl;
    private string speakStopUrl;

    // Text chat toggle
    private bool textChatVisible = false;

    // Speech bubble visibility toggle — when true the bubble is fully suppressed
    private bool _hideBubble = false;
    private GameObject _bubbleToggleButton;
    private TextMeshProUGUI _bubbleToggleIcon;
    private GameObject _bubbleHiddenBadge;

    // Voice output toggle — when false no TTS requests/playback are performed
    private bool _voiceOutputEnabled = true;
    private const string VoiceOutputPrefKey = "companion_voice_output_enabled";
    private GameObject _voiceToggleButton;
    private TextMeshProUGUI _voiceToggleIcon;

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
    private Image  _bubbleBgImage;     // cached speech bubble background for mood tinting
    private AudioSource _ttsAudioSource;
    private float  _lastInteractionTime;
    private const float ProactiveIdleDelay = 300f;  // 5 min silence → character speaks up
    private float _lastUserActivityTime;
    private Vector3 _lastMousePos;
    private float _nextProactiveAt;

    // Exposed for VoiceInputManager
    public Image  MicButtonImage    => micButtonImage;
    public Color  InputBgColor      => BgInput;

    void Start()
    {
        streamUrl = GetApiUrl("/chat/stream");
        clearUrl = GetApiUrl("/memory/clear");
        speakUrl = GetApiUrl("/speak");
        desktopContextUrl = GetApiUrl("/desktop/context");
        healthUrl = GetApiUrl("/character");
        speakStopUrl = GetApiUrl("/speak/stop");

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
        BuildBubbleToggle(canvas);
        BuildVoiceToggle(canvas);
        BuildBubbleHiddenBadge(canvas);
        BuildStatusDot(canvas);

        _voiceOutputEnabled = PlayerPrefs.GetInt(VoiceOutputPrefKey, 1) == 1;
        UpdateVoiceToggleVisuals();

        UpdateCharacterNameUI();
        SetTextChatVisible(false);

        // Auto-attach VoiceInputManager if not already present
        if (GetComponent<VoiceInputManager>() == null)
            gameObject.AddComponent<VoiceInputManager>();

        // Start periodic backend health-check
        StartCoroutine(HealthCheckLoop());

        // Proactive idle messages — character speaks up after 5 min of silence
        _lastInteractionTime = Time.time;
        _lastUserActivityTime = Time.time;
        _lastMousePos = Input.mousePosition;
        _nextProactiveAt = Time.time + Random.Range(190f, 340f);
        StartCoroutine(ProactiveMessageLoop());

        EnsureTtsAudioSource();
    }

    private void EnsureTtsAudioSource()
    {
        if (_ttsAudioSource != null) return;
        _ttsAudioSource = GetComponent<AudioSource>();
        if (_ttsAudioSource == null)
            _ttsAudioSource = gameObject.AddComponent<AudioSource>();
        _ttsAudioSource.playOnAwake = false;
        _ttsAudioSource.loop = false;
        _ttsAudioSource.spatialBlend = 0f;
    }

    public string GetApiUrl(string path)
    {
        string baseUrl = (backendBaseUrl ?? "").Trim();
        if (string.IsNullOrEmpty(baseUrl))
            baseUrl = "http://127.0.0.1:5001";

        baseUrl = baseUrl.TrimEnd('/');
        if (string.IsNullOrEmpty(path)) return baseUrl;
        if (!path.StartsWith("/")) path = "/" + path;
        return baseUrl + path;
    }

    // ── Speech bubble ────────────────────────────────────────────────────────

    /// <summary>
    /// Show the speech bubble with the given text and restart the auto-dismiss timer.
    /// All code that wants to display something in the bubble should call this.
    /// </summary>
    private void ShowBubble(string text)
    {
        if (_hideBubble) return;  // bubble suppressed by user toggle
        if (speechBubbleText != null) speechBubbleText.text = text;
        if (speechBubble == null) return;

        // Reset to default tint + full opacity before showing
        if (_bubbleBgImage != null) _bubbleBgImage.color = new Color(BgBubble.r, BgBubble.g, BgBubble.b, 1f);
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
        if (_hideBubble) return;  // bubble suppressed by user toggle
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

        float elapsed = 0f;
        while (elapsed < BubbleFadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / BubbleFadeTime);
            if (_bubbleBgImage != null)
                _bubbleBgImage.color = new Color(_bubbleBgImage.color.r, _bubbleBgImage.color.g, _bubbleBgImage.color.b, alpha);
            if (speechBubbleText != null)
                speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            yield return null;
        }

        speechBubble.SetActive(false);
        // Restore full opacity so it looks right next time it appears
        if (_bubbleBgImage != null)
            _bubbleBgImage.color = new Color(_bubbleBgImage.color.r, _bubbleBgImage.color.g, _bubbleBgImage.color.b, 1f);
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
        _bubbleBgImage = bg;  // cache for mood tinting

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
                // onClick is wired in the Inspector — don't add a second listener here
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

    // ── Bubble visibility toggle button ──────────────────────────────────────
    private void BuildBubbleToggle(Canvas canvas)
    {
        if (canvas == null) return;

        _bubbleToggleButton = new GameObject("BubbleToggleButton");
        _bubbleToggleButton.transform.SetParent(canvas.transform, false);

        var rect = _bubbleToggleButton.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot     = new Vector2(1f, 0f);
        // Sits directly left of the Chat toggle button (which is at -12, 12, w=52)
        rect.anchoredPosition = new Vector2(-72f, 12f);
        rect.sizeDelta = new Vector2(36f, 36f);

        var bg = _bubbleToggleButton.AddComponent<Image>();
        bg.color = BgButton;
        MakeRounded(bg);

        var btn = _bubbleToggleButton.AddComponent<Button>();
        var cols = btn.colors;
        cols.normalColor      = BgButton;
        cols.highlightedColor = new Color(0.20f, 0.22f, 0.32f, 1f);
        cols.pressedColor     = new Color(0.27f, 0.28f, 0.38f, 1f);
        cols.fadeDuration     = 0.08f;
        btn.colors = cols;
        btn.onClick.AddListener(OnToggleBubble);

        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(_bubbleToggleButton.transform, false);
        var iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
        iconRect.sizeDelta = Vector2.zero; iconRect.offsetMin = Vector2.zero; iconRect.offsetMax = Vector2.zero;

        _bubbleToggleIcon = iconObj.AddComponent<TextMeshProUGUI>();
        _bubbleToggleIcon.text      = "💬";
        _bubbleToggleIcon.fontSize  = 14f;
        _bubbleToggleIcon.alignment = TextAlignmentOptions.Center;
        _bubbleToggleIcon.enableWordWrapping = false;

        UpdateBubbleHiddenBadgeVisibility();
    }

    // ── Voice output toggle button ───────────────────────────────────────────
    private void BuildVoiceToggle(Canvas canvas)
    {
        if (canvas == null) return;

        _voiceToggleButton = new GameObject("VoiceToggleButton");
        _voiceToggleButton.transform.SetParent(canvas.transform, false);

        var rect = _voiceToggleButton.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot     = new Vector2(1f, 0f);
        // Sits left of bubble toggle button
        rect.anchoredPosition = new Vector2(-112f, 12f);
        rect.sizeDelta = new Vector2(36f, 36f);

        var bg = _voiceToggleButton.AddComponent<Image>();
        bg.color = BgButton;
        MakeRounded(bg);

        var btn = _voiceToggleButton.AddComponent<Button>();
        var cols = btn.colors;
        cols.normalColor      = BgButton;
        cols.highlightedColor = new Color(0.20f, 0.22f, 0.32f, 1f);
        cols.pressedColor     = new Color(0.27f, 0.28f, 0.38f, 1f);
        cols.fadeDuration     = 0.08f;
        btn.colors = cols;
        btn.onClick.AddListener(OnToggleVoiceOutput);

        var iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(_voiceToggleButton.transform, false);
        var iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
        iconRect.sizeDelta = Vector2.zero; iconRect.offsetMin = Vector2.zero; iconRect.offsetMax = Vector2.zero;

        _voiceToggleIcon = iconObj.AddComponent<TextMeshProUGUI>();
        _voiceToggleIcon.text      = "🔊";
        _voiceToggleIcon.fontSize  = 14f;
        _voiceToggleIcon.alignment = TextAlignmentOptions.Center;
        _voiceToggleIcon.enableWordWrapping = false;
    }

    private void BuildBubbleHiddenBadge(Canvas canvas)
    {
        if (canvas == null) return;

        _bubbleHiddenBadge = new GameObject("BubbleHiddenBadge");
        _bubbleHiddenBadge.transform.SetParent(canvas.transform, false);

        var rect = _bubbleHiddenBadge.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot     = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-168f, 16f); // left of bottom-right control cluster
        rect.sizeDelta = new Vector2(100f, 22f);

        var bg = _bubbleHiddenBadge.AddComponent<Image>();
        bg.color = new Color(0.30f, 0.12f, 0.12f, 0.88f);
        MakeRounded(bg);

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(_bubbleHiddenBadge.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 0f);
        textRect.offsetMax = new Vector2(-8f, 0f);

        var label = textObj.AddComponent<TextMeshProUGUI>();
        label.text = "Bubble Hidden";
        label.fontSize = 10f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.86f, 0.86f, 1f);
        label.enableWordWrapping = false;

        _bubbleHiddenBadge.SetActive(false);
    }

    private void UpdateBubbleHiddenBadgeVisibility()
    {
        if (_bubbleHiddenBadge == null) return;
        _bubbleHiddenBadge.SetActive(_hideBubble && !textChatVisible);
    }

    private void OnToggleBubble()
    {
        _hideBubble = !_hideBubble;

        // Hide the bubble immediately when suppressed
        if (_hideBubble)
        {
            if (_bubbleDismissTimer != null) { StopCoroutine(_bubbleDismissTimer); _bubbleDismissTimer = null; }
            if (speechBubble != null) speechBubble.SetActive(false);
        }

        // Update button appearance: dimmed + strikethrough feel when hidden
        if (_bubbleToggleButton != null)
        {
            var bg = _bubbleToggleButton.GetComponent<Image>();
            if (bg != null)
                bg.color = _hideBubble
                    ? new Color(0.22f, 0.10f, 0.10f, 0.90f)  // dim red tint = bubble off
                    : BgButton;                                // normal
        }
        if (_bubbleToggleIcon != null)
            _bubbleToggleIcon.color = _hideBubble
                ? new Color(0.55f, 0.25f, 0.25f, 1f)   // muted red
                : TextPrimary;

        UpdateBubbleHiddenBadgeVisibility();
    }

    private void OnToggleVoiceOutput()
    {
        _voiceOutputEnabled = !_voiceOutputEnabled;
        PlayerPrefs.SetInt(VoiceOutputPrefKey, _voiceOutputEnabled ? 1 : 0);
        PlayerPrefs.Save();

        if (!_voiceOutputEnabled)
            StopSpeech();

        UpdateVoiceToggleVisuals();
    }

    private void UpdateVoiceToggleVisuals()
    {
        if (_voiceToggleButton != null)
        {
            var bg = _voiceToggleButton.GetComponent<Image>();
            if (bg != null)
                bg.color = _voiceOutputEnabled
                    ? BgButton
                    : new Color(0.22f, 0.10f, 0.10f, 0.90f);
        }
        if (_voiceToggleIcon != null)
        {
            _voiceToggleIcon.text = _voiceOutputEnabled ? "🔊" : "🔇";
            _voiceToggleIcon.color = _voiceOutputEnabled
                ? TextPrimary
                : new Color(0.55f, 0.25f, 0.25f, 1f);
        }
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
            using (UnityWebRequest req = UnityWebRequest.Get(healthUrl))
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
        // Track user activity continuously (for quiet-aware proactive timing)
        if ((Input.mousePosition - _lastMousePos).sqrMagnitude > 2f)
        {
            _lastUserActivityTime = Time.time;
            _lastMousePos = Input.mousePosition;
        }
        if (Input.anyKeyDown)
            _lastUserActivityTime = Time.time;

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

        // Bubble toggle also hides when the panel is open — it sits next to the Chat button
        if (_bubbleToggleButton != null)
            _bubbleToggleButton.SetActive(!visible);

        // Voice output toggle follows the same HUD visibility rule as bubble toggle
        if (_voiceToggleButton != null)
            _voiceToggleButton.SetActive(!visible);

        UpdateBubbleHiddenBadgeVisibility();

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
        string url = GetApiUrl($"/character?id={charId}");

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
        _lastInteractionTime = Time.time;
        StartCoroutine(SendToAI(message));
    }

    IEnumerator SendToAI(string message)
    {
        StopSpeech(); // interrupt any ongoing TTS before processing new message

        // Show animated loading dots while waiting for the first token.
        // Cancel any running dismiss timer but do NOT start a new one; the bubble must stay
        // until the response arrives (which can take several seconds on local Ollama).
        if (!_hideBubble && speechBubble != null)
        {
            var loadImg = speechBubble.GetComponent<Image>();
            if (loadImg != null) loadImg.color = new Color(loadImg.color.r, loadImg.color.g, loadImg.color.b, 1f);
            if (speechBubbleText != null)
                speechBubbleText.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, 1f);
            if (_bubbleDismissTimer != null) { StopCoroutine(_bubbleDismissTimer); _bubbleDismissTimer = null; }
            speechBubble.SetActive(true);
        }
        if (_dotCoroutine != null) StopCoroutine(_dotCoroutine);
        if (!_hideBubble)
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
            bool doneHandled = false;

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
                                    doneHandled = true;

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
                                            .Replace(safeReply, @"\[e:[a-z]+\]", "");
                                        safeReply = System.Text.RegularExpressions.Regex
                                            .Replace(safeReply, @"\s{2,}", " ").Trim();
                                        if (_dotCoroutine != null) { StopCoroutine(_dotCoroutine); _dotCoroutine = null; }
                                        ShowBubble(safeReply);
                                        TintBubble(done.emotion);
                                        StartCoroutine(SpeakReply(safeReply, charName, done.emotion));
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
                                        // Strip LLM emotion tags [e:joy] etc — must not appear as text
                                        display = System.Text.RegularExpressions.Regex.Replace(
                                            display, @"\[e:[a-z]+\]", "");
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

            // Fallback: if request succeeded but we never parsed the done event
            // (can happen when SSE chunks split JSON lines), recover from full buffer.
            if (req.result == UnityWebRequest.Result.Success && !doneHandled)
            {
                string allData = req.downloadHandler?.text ?? "";
                string[] lines = allData.Split('\n');
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    string trimmed = lines[i].Trim();
                    if (!trimmed.StartsWith("data: ")) continue;
                    string jsonStr = trimmed.Substring(6);
                    bool isDoneEvent = jsonStr.Contains("\"done\":true") || jsonStr.Contains("\"done\": true");
                    if (!isDoneEvent) continue;

                    try
                    {
                        StreamDone done = JsonUtility.FromJson<StreamDone>(jsonStr);
                        doneHandled = true;

                        if (emoteAnimator != null)
                            emoteAnimator.StopTalkingGestures();

                        if (done.emotes != null && done.emotes.Length > 0)
                            TriggerEmotes(done.emotes);

                        if (!string.IsNullOrEmpty(done.emotion))
                            TriggerEmotion(done.emotion);

                        if (!string.IsNullOrEmpty(done.reply))
                        {
                            string safeReply = System.Text.RegularExpressions.Regex
                                .Replace(done.reply, @"\*[^*]+\*", "");
                            safeReply = System.Text.RegularExpressions.Regex
                                .Replace(safeReply, @"\[e:[a-z]+\]", "");
                            safeReply = System.Text.RegularExpressions.Regex
                                .Replace(safeReply, @"\s{2,}", " ").Trim();
                            if (_dotCoroutine != null) { StopCoroutine(_dotCoroutine); _dotCoroutine = null; }
                            ShowBubble(safeReply);
                            TintBubble(done.emotion);
                            StartCoroutine(SpeakReply(safeReply, charName, done.emotion));
                        }

                        if (faceAnim != null)
                            faceAnim.StopTalking();
                        break;
                    }
                    catch { /* ignore malformed fallback line and keep scanning */ }
                }
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

            // Safety net: never leave UI/input locked after this coroutine exits.
            if (_isStreaming)
                _isStreaming = false;
            SetInputLocked(false);
            if (_dotCoroutine != null) { StopCoroutine(_dotCoroutine); _dotCoroutine = null; }
            if (faceAnim != null)
                faceAnim.StopTalking();
            if (emoteAnimator != null && emoteAnimator.IsPlaying)
                emoteAnimator.StopTalkingGestures();
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
    IEnumerator SpeakReply(string text, string character, string emotion = "neutral")
    {
        if (!_voiceOutputEnabled)
            yield break;

        string body = JsonUtility.ToJson(new SpeakRequest { text = text, character = character, emotion = emotion });
        byte[] raw  = System.Text.Encoding.UTF8.GetBytes(body);
        using (UnityWebRequest req = new UnityWebRequest(speakUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                yield break;

            SpeakResponse resp = null;
            try { resp = JsonUtility.FromJson<SpeakResponse>(req.downloadHandler.text); }
            catch { }

            // Hosted backend returns audio_url for client-side playback
            if (resp != null && resp.ok && !string.IsNullOrEmpty(resp.audio_url))
                yield return StartCoroutine(PlayRemoteTtsAudio(resp.audio_url));
        }
    }

    private IEnumerator PlayRemoteTtsAudio(string audioUrl)
    {
        EnsureTtsAudioSource();
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.MPEG))
        {
            req.timeout = 20;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                yield break;

            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            if (clip == null) yield break;

            _ttsAudioSource.Stop();
            _ttsAudioSource.clip = clip;
            _ttsAudioSource.Play();
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

        // Mood-reactive idle — smoothly shift ambient motion to match emotion
        var idleAnim = CharacterManager.Instance?.GetIdleAnimator();
        if (idleAnim != null)
            idleAnim.SetMood(emotion);

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

    /// <summary>
    /// Subtly tint the speech bubble background based on detected emotion.
    /// joy=warm rose, sorrow=cool blue, angry=amber, fun=soft violet, neutral=default navy.
    /// </summary>
    private void TintBubble(string emotion)
    {
        if (_bubbleBgImage == null) return;
        Color tint;
        switch (emotion?.ToLower() ?? "neutral")
        {
            case "joy":    tint = new Color(0.12f, 0.07f, 0.10f, 0.90f); break; // warm rose
            case "fun":    tint = new Color(0.10f, 0.07f, 0.13f, 0.90f); break; // soft violet
            case "angry":  tint = new Color(0.13f, 0.09f, 0.05f, 0.90f); break; // amber
            case "sorrow": tint = new Color(0.05f, 0.07f, 0.13f, 0.90f); break; // cool blue
            default:       tint = BgBubble;                               break;
        }
        _bubbleBgImage.color = tint;
    }

    /// <summary>
    /// Fire-and-forget: stop any in-progress TTS by posting to /speak/stop.
    /// </summary>
    private void StopSpeech()
    {
        if (_ttsAudioSource != null && _ttsAudioSource.isPlaying)
            _ttsAudioSource.Stop();
        StartCoroutine(PostSpeakStop());
    }

    private IEnumerator PostSpeakStop()
    {
        using (UnityWebRequest req = new UnityWebRequest(speakStopUrl, "POST"))
        {
            req.uploadHandler   = new UploadHandlerRaw(new byte[0]);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
        }
    }

    /// <summary>
    /// Checks every 30s whether the user has been silent for ProactiveIdleDelay seconds.
    /// If so, fetches a short in-character check-in message and shows/speaks it.
    /// </summary>
    private IEnumerator ProactiveMessageLoop()
    {
        yield return new WaitForSeconds(30f); // initial grace period
        while (true)
        {
            yield return new WaitForSeconds(10f);
            if (_isStreaming) continue;
            if (Time.time - _lastInteractionTime < ProactiveIdleDelay) continue;
            if (Time.time - _lastUserActivityTime < 45f) continue; // actively using keyboard/mouse

            // Quiet hours: be less chatty late night / very early morning
            int hour = System.DateTime.Now.Hour;
            bool quietHours = (hour >= 23 || hour < 8);
            if (quietHours && Time.time - _lastInteractionTime < 900f) continue;

            if (Time.time < _nextProactiveAt) continue;
            string charName = CharacterManager.Instance != null
                ? CharacterManager.Instance.GetCurrentCharacterName()
                : "female_default";
            yield return SendProactiveMessage(charName);
            _nextProactiveAt = Time.time + Random.Range(220f, 460f);
        }
    }

    private IEnumerator SendProactiveMessage(string charName)
    {
        // Pull lightweight desktop context from backend, then pass it into idle generation.
        string appName = "";
        using (UnityWebRequest ctxReq = UnityWebRequest.Get(desktopContextUrl))
        {
            ctxReq.timeout = 2;
            yield return ctxReq.SendWebRequest();
            if (ctxReq.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var ctx = JsonUtility.FromJson<DesktopContextInfo>(ctxReq.downloadHandler.text);
                    if (ctx != null) appName = ctx.app;
                }
                catch { }
            }
        }

        string encodedApp = UnityWebRequest.EscapeURL(appName ?? "");
        string url = GetApiUrl($"/idle?character={charName}&app={encodedApp}");

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = 8;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;
            try
            {
                var resp = JsonUtility.FromJson<ChatResponse>(req.downloadHandler.text);
                if (resp == null || string.IsNullOrEmpty(resp.reply)) yield break;
                ShowBubble(resp.reply);
                TintBubble(resp.emotion);
                StartCoroutine(SpeakReply(resp.reply, charName, resp.emotion));
                _lastUserActivityTime = Time.time;
                if (resp.emotes != null && resp.emotes.Length > 0)
                    TriggerEmotes(resp.emotes);
                _lastInteractionTime = Time.time; // reset so we don't fire again immediately
            }
            catch { /* ignore malformed response */ }
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
        public string emotion;
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
        public string emotion;
    }

    [System.Serializable]
    private class SpeakResponse
    {
        public bool ok;
        public string audio_url;
        public string note;
        public string error;
    }

    [System.Serializable]
    private class DesktopContextInfo
    {
        public string app;
        public string title;
    }

    [System.Serializable]
    private class CharacterInfo
    {
        public string name;
        public string tone;
    }
}
