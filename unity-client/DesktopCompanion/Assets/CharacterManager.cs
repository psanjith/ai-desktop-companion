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

    // Loaded character
    private GameObject currentModel;

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
        // Luna defaults (after tuning pass): headTilt=2.0, headTurn=3.0, sway=1.2, armSway=0.8
        // Ren is ~50% on tilt/turn and ~40% on sway/arm to stay composed and deliberate.
        var idleAnim = currentModel.GetComponent<IdleAnimator>();
        if (idleAnim != null && isReserved)
        {
            idleAnim.headTiltAmount = 1.0f; // Luna=2.0  — Ren barely tilts his head
            idleAnim.headTurnAmount = 1.8f; // Luna=3.0  — Ren looks around less
            idleAnim.swayAmount     = 0.5f; // Luna=1.2  — Ren stands more composed
            idleAnim.armSwayAmount  = 0.3f; // Luna=0.8  — Ren's arms are stiller at rest
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
            // Whole-body motion
            idle.bobAmount = 0.04f;
            idle.bobSpeed = 1.2f;
            idle.breatheAmount = 0.008f;
            idle.breatheSpeed = 2.5f;

            // Bone-level idle (spine sway, head look-around, arm micro-movement)
            idle.swayAmount = 3f;
            idle.swaySpeed = 0.6f;
            idle.weightShiftAmount = 2f;
            idle.weightShiftSpeed = 0.4f;
            idle.headTiltAmount = 4f;
            idle.headTiltSpeed = 0.35f;
            idle.headTurnAmount = 6f;
            idle.headTurnSpeed = 0.25f;
            idle.armSwayAmount = 2f;
            idle.armSwaySpeed = 0.8f;
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
