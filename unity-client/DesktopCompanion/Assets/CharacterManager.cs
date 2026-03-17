using System.IO;
using System.Collections;
using UnityEngine;

public class CharacterManager : MonoBehaviour
{
    [Header("Character Setup")]
    [Tooltip("Drag your character prefabs here (import VRM as prefab first)")]
    public GameObject[] characterPrefabs;

    [Header("Character State")]
    public int currentCharacterIndex = 0;

    [Header("Size Settings")]
    public float minScale = 0.3f;
    public float maxScale = 3.0f;
    public float scaleStep = 0.2f;
    private float currentScale = 1.0f;

    [Header("Screen Presence")]
    [Tooltip("Makes the character subtly follow the cursor and perch on screen edges over time.")]
    public bool enableScreenPresence = true;
    public float cursorFollowRangeX = 0.22f;
    public float perchRangeX = 0.30f;
    public float presenceSmoothing = 2.8f;

    // Loaded character
    private GameObject currentModel;
    private Vector3 _managerBaseLocalPos;
    private float _perchTargetX = 0f;
    private float _nextPerchChangeAt = 0f;
    private Vector3 _lastMousePos;
    private float _lastMouseMoveAt = 0f;

    // Singleton for easy access
    public static CharacterManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        _managerBaseLocalPos = transform.localPosition;
        _lastMousePos = Input.mousePosition;
        _lastMouseMoveAt = Time.time;

        if (characterPrefabs == null || characterPrefabs.Length == 0)
        {
            Debug.LogWarning("CharacterManager: No character prefabs assigned! Drag them in the Inspector.");
            return;
        }

        // Load saved preference
        currentCharacterIndex = PlayerPrefs.GetInt("SelectedCharacter", 0);
        if (currentCharacterIndex >= characterPrefabs.Length)
            currentCharacterIndex = 0;

        // Load saved scale
        currentScale = PlayerPrefs.GetFloat("CharacterScale", 1.0f);

        Debug.Log($"CharacterManager: {characterPrefabs.Length} character(s) available");
        LoadCharacter(currentCharacterIndex);
    }

    void Update()
    {
        if (!enableScreenPresence || currentModel == null) return;

        // Track user activity from mouse movement to avoid fighting active interaction.
        Vector3 mp = Input.mousePosition;
        if ((mp - _lastMousePos).sqrMagnitude > 4f)
        {
            _lastMouseMoveAt = Time.time;
            _lastMousePos = mp;
        }

        if (Time.time >= _nextPerchChangeAt && Time.time - _lastMouseMoveAt > 4f)
            ChooseNextPerch();

        float halfW = Mathf.Max(1f, Screen.width * 0.5f);
        float nx = Mathf.Clamp((Input.mousePosition.x - halfW) / halfW, -1f, 1f);

        // Blend persistent perch target with cursor following so it feels intentional, not twitchy.
        float followX = nx * cursorFollowRangeX;
        float targetX = _managerBaseLocalPos.x + (_perchTargetX * 0.55f) + (followX * 0.45f);

        Vector3 p = transform.localPosition;
        p.x = Mathf.Lerp(p.x, targetX, 1f - Mathf.Exp(-presenceSmoothing * Time.deltaTime));
        transform.localPosition = p;
    }

    private void ChooseNextPerch()
    {
        float[] anchors = new float[] { -perchRangeX, 0f, perchRangeX };
        _perchTargetX = anchors[Random.Range(0, anchors.Length)] + Random.Range(-0.04f, 0.04f);
        _nextPerchChangeAt = Time.time + Random.Range(18f, 36f);
    }

    public void LoadCharacter(int index)
    {
        if (characterPrefabs == null || index < 0 || index >= characterPrefabs.Length)
        {
            Debug.LogError("CharacterManager: Invalid character index");
            return;
        }

        if (characterPrefabs[index] == null)
        {
            Debug.LogError($"CharacterManager: Prefab at index {index} is null");
            return;
        }

        // Destroy current model if exists
        if (currentModel != null)
        {
            Destroy(currentModel);
        }

        // Instantiate the prefab
        currentModel = Instantiate(characterPrefabs[index], this.transform);
        currentModel.transform.localPosition = Vector3.zero;
        currentModel.transform.localRotation = Quaternion.identity;
        currentModel.transform.localScale = Vector3.one * currentScale;

        // Reset presence drift each time we swap character.
        transform.localPosition = _managerBaseLocalPos;
        _perchTargetX = 0f;
        _nextPerchChangeAt = Time.time + Random.Range(8f, 16f);

        // Add idle animation
        SetupIdleAnimation(currentModel);

        // Determine personality style from prefab name:
        //   contains "male" (not "female") → Reserved (Ren: subtle, deliberate)
        //   everything else               → Expressive (Luna: big, varied)
        string prefabId = characterPrefabs[index].name.ToLower();
        bool isReserved = prefabId.Contains("male") && !prefabId.Contains("female");

        var emoteAnim = currentModel.GetComponent<EmoteAnimator>();
        if (emoteAnim != null)
            emoteAnim.style = isReserved
                ? EmoteAnimator.CharacterStyle.Reserved
                : EmoteAnimator.CharacterStyle.Expressive;

        // Tune idle animation amplitude to match personality.
        // Luna: headTilt=1.2, headTurn=1.5, sway=0.5, armSway=0.22, boneSmoothing=1.8
        // Ren is ~40% on head/sway/arm — composed, still, deliberate.
        var idleAnim = currentModel.GetComponent<IdleAnimator>();
        if (idleAnim != null && isReserved)
        {
            idleAnim.headTiltAmount = 0.5f;  // Luna=1.2 — Ren barely tilts
            idleAnim.headTurnAmount = 0.6f;  // Luna=1.5 — Ren rarely looks around
            idleAnim.swayAmount     = 0.2f;  // Luna=0.5 — Ren stands very still
            idleAnim.armSwayAmount  = 0.08f; // Luna=0.22 — arms almost motionless
            idleAnim.boneSmoothing  = 1.4f;  // even heavier — Ren moves deliberately
        }

        currentCharacterIndex = index;
        PlayerPrefs.SetInt("SelectedCharacter", index);
        PlayerPrefs.Save();

        Debug.Log($"CharacterManager: Loaded {characterPrefabs[index].name}");
    }

    public void SwitchCharacter()
    {
        if (characterPrefabs == null || characterPrefabs.Length <= 1) return;
        int nextIndex = (currentCharacterIndex + 1) % characterPrefabs.Length;
        LoadCharacter(nextIndex);
    }

    public string GetCurrentCharacterName()
    {
        if (characterPrefabs != null && currentCharacterIndex < characterPrefabs.Length
            && characterPrefabs[currentCharacterIndex] != null)
            return characterPrefabs[currentCharacterIndex].name;
        return "None";
    }

    public int GetCharacterCount()
    {
        return characterPrefabs != null ? characterPrefabs.Length : 0;
    }

    public void ScaleUp()
    {
        SetScale(currentScale + scaleStep);
    }

    public void ScaleDown()
    {
        SetScale(currentScale - scaleStep);
    }

    public void SetScale(float scale)
    {
        currentScale = Mathf.Clamp(scale, minScale, maxScale);
        if (currentModel != null)
        {
            currentModel.transform.localScale = Vector3.one * currentScale;

            // Tell IdleAnimator about the new base scale
            var idle = currentModel.GetComponent<IdleAnimator>();
            if (idle != null)
                idle.SetBaseScale(currentScale);
        }

        PlayerPrefs.SetFloat("CharacterScale", currentScale);
        PlayerPrefs.Save();
        Debug.Log($"CharacterManager: Scale = {currentScale:F1}");
    }

    public float GetScale()
    {
        return currentScale;
    }

    public EmoteAnimator GetEmoteAnimator()
    {
        if (currentModel == null) return null;
        return currentModel.GetComponent<EmoteAnimator>();
    }

    public FaceAnimator GetFaceAnimator()
    {
        if (currentModel == null) return null;
        return currentModel.GetComponent<FaceAnimator>();
    }

    public IdleAnimator GetIdleAnimator()
    {
        if (currentModel == null) return null;
        return currentModel.GetComponent<IdleAnimator>();
    }

    public PoseManager GetPoseManager()
    {
        if (currentModel == null) return null;
        return currentModel.GetComponent<PoseManager>();
    }

    private void SetupIdleAnimation(GameObject model)
    {
        // Fix T-pose: apply natural rest pose FIRST (before other scripts read positions)
        var pose = model.AddComponent<PoseManager>();

        // Add a simple breathing/bobbing idle animation
        var idle = model.AddComponent<IdleAnimator>();
        if (idle != null)
        {
            // Luna defaults — natural, human-like standing rest motion
            idle.bobAmount         = 0.006f;  // barely perceptible up/down
            idle.bobSpeed          = 0.28f;
            idle.breatheAmount     = 0.0015f;
            idle.breatheSpeed      = 0.55f;
            idle.swayAmount        = 0.5f;    // very subtle spine sway
            idle.swaySpeed         = 0.13f;
            idle.weightShiftAmount = 0.3f;
            idle.weightShiftSpeed  = 0.08f;
            idle.headTiltAmount    = 1.2f;    // occasional micro-tilt
            idle.headTiltSpeed     = 0.08f;
            idle.headTurnAmount    = 1.5f;    // rare, slow look-arounds
            idle.headTurnSpeed     = 0.05f;
            idle.armSwayAmount     = 0.22f;   // arms barely drift — most important for natural feel
            idle.armSwaySpeed      = 0.12f;
            idle.elbowFlexAmount   = 0.35f;
            idle.elbowFlexSpeed    = 0.09f;
            idle.boneSmoothing     = 1.8f;    // heavier inertia — bones feel weighted, not snappy
        }

        // Add emote animator for LLM-triggered animations
        var emote = model.AddComponent<EmoteAnimator>();
        if (emote != null)
        {
            emote.CaptureOriginalState();
        }

        // Add face animator for VRM blend shape expressions
        var face = model.AddComponent<FaceAnimator>();
        if (face != null)
        {
            face.blinkInterval = 3.5f;
            face.expressionHoldTime = 4f;
        }
    }
}
