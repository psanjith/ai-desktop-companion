using UnityEngine;

/// <summary>
/// Rich idle animation — bobbing, breathing, head look-around, body sway,
/// weight shifting, and arm micro-movements using humanoid bones.
/// Gives the character a natural "alive" feel even when not emoting.
/// Attached automatically by CharacterManager when a model loads.
/// </summary>
public class IdleAnimator : MonoBehaviour
{
    [Header("Bobbing (up/down)")]
    public float bobAmount = 0.04f;
    public float bobSpeed = 1.2f;

    [Header("Breathing (chest scale pulse)")]
    public float breatheAmount = 0.008f;
    public float breatheSpeed = 2.5f;

    [Header("Body Sway (side-to-side lean)")]
    public float swayAmount = 3f;       // degrees
    public float swaySpeed = 0.6f;

    [Header("Weight Shift (hips)")]
    public float weightShiftAmount = 2f; // degrees
    public float weightShiftSpeed = 0.4f;

    [Header("Head Movement")]
    public float headTiltAmount = 4f;    // degrees
    public float headTiltSpeed = 0.35f;
    public float headTurnAmount = 6f;    // degrees
    public float headTurnSpeed = 0.25f;

    [Header("Arm Micro-Movement")]
    public float armSwayAmount = 2f;     // degrees
    public float armSwaySpeed = 0.8f;

    private Vector3 startPos;
    private float baseScale = 1f;

    // Bone references (cached for performance)
    private Animator anim;
    private Transform headBone;
    private Transform spineBone;
    private Transform hipsBone;
    private Transform leftUpperArm;
    private Transform rightUpperArm;
    private Transform leftHand;
    private Transform rightHand;

    // Rest rotations (captured after PoseManager sets the natural pose)
    private Quaternion headRest;
    private Quaternion spineRest;
    private Quaternion hipsRest;
    private Quaternion leftArmRest;
    private Quaternion rightArmRest;
    private Quaternion leftHandRest;
    private Quaternion rightHandRest;

    private bool bonesReady = false;

    /// <summary>
    /// When true, idle bone rotations are paused (emote is playing).
    /// Transform-level bob/breathe still runs for subtle aliveness.
    /// </summary>
    public bool paused = false;

    // Random phase offset so two characters don't move in sync
    private float phaseOffset;

    void Start()
    {
        startPos = transform.localPosition;
        baseScale = transform.localScale.x;
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);

        // Delay bone caching by a frame so PoseManager applies the rest pose first
        Invoke(nameof(CacheBones), 0.1f);
    }

    void CacheBones()
    {
        anim = GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman) return;

        headBone     = anim.GetBoneTransform(HumanBodyBones.Head);
        spineBone    = anim.GetBoneTransform(HumanBodyBones.Spine);
        hipsBone     = anim.GetBoneTransform(HumanBodyBones.Hips);
        leftUpperArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        leftHand     = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand    = anim.GetBoneTransform(HumanBodyBones.RightHand);

        // Snapshot rest rotations (PoseManager should have applied these already)
        if (headBone != null)      headRest      = headBone.localRotation;
        if (spineBone != null)     spineRest     = spineBone.localRotation;
        if (hipsBone != null)      hipsRest      = hipsBone.localRotation;
        if (leftUpperArm != null)  leftArmRest   = leftUpperArm.localRotation;
        if (rightUpperArm != null) rightArmRest  = rightUpperArm.localRotation;
        if (leftHand != null)      leftHandRest  = leftHand.localRotation;
        if (rightHand != null)     rightHandRest = rightHand.localRotation;

        bonesReady = true;
    }

    void Update()
    {
        float t = Time.time + phaseOffset;

        // ---- Whole-body: bob + breathe ----
        float bobOffset = Mathf.Sin(t * bobSpeed) * bobAmount;
        transform.localPosition = startPos + new Vector3(0f, bobOffset, 0f);

        float breatheOffset = Mathf.Sin(t * breatheSpeed) * breatheAmount;
        float s = baseScale + breatheOffset;
        transform.localScale = new Vector3(s, s, s);

        if (!bonesReady || paused) return; // Skip bone rotations when emote is playing

        // ---- Spine: gentle side sway (shifting weight feel) ----
        if (spineBone != null)
        {
            float sway = Mathf.Sin(t * swaySpeed) * swayAmount;
            float lean = Mathf.Sin(t * swaySpeed * 0.7f) * (swayAmount * 0.3f);
            spineBone.localRotation = spineRest * Quaternion.Euler(lean, 0f, sway);
        }

        // ---- Hips: subtle rotation for weight shift ----
        if (hipsBone != null)
        {
            float shift = Mathf.Sin(t * weightShiftSpeed) * weightShiftAmount;
            hipsBone.localRotation = hipsRest * Quaternion.Euler(0f, shift, 0f);
        }

        // ---- Head: slow look-around (tilt + turn on different phases) ----
        if (headBone != null)
        {
            float tilt = Mathf.Sin(t * headTiltSpeed) * headTiltAmount;
            float turn = Mathf.Sin(t * headTurnSpeed + 1.5f) * headTurnAmount;
            float nod  = Mathf.Sin(t * headTiltSpeed * 0.6f + 0.8f) * (headTiltAmount * 0.5f);
            headBone.localRotation = headRest * Quaternion.Euler(nod, turn, tilt);
        }

        // ---- Arms: gentle micro-sway (natural hanging movement) ----
        if (leftUpperArm != null)
        {
            float armSway = Mathf.Sin(t * armSwaySpeed + 0.5f) * armSwayAmount;
            leftUpperArm.localRotation = leftArmRest * Quaternion.Euler(armSway, 0f, armSway * 0.3f);
        }
        if (rightUpperArm != null)
        {
            float armSway = Mathf.Sin(t * armSwaySpeed + 2.0f) * armSwayAmount;
            rightUpperArm.localRotation = rightArmRest * Quaternion.Euler(armSway, 0f, -armSway * 0.3f);
        }

        // ---- Hands: very subtle fidget ----
        if (leftHand != null)
        {
            float fidget = Mathf.Sin(t * 1.8f + 1f) * 1.5f;
            leftHand.localRotation = leftHandRest * Quaternion.Euler(fidget, 0f, 0f);
        }
        if (rightHand != null)
        {
            float fidget = Mathf.Sin(t * 1.6f + 3f) * 1.5f;
            rightHand.localRotation = rightHandRest * Quaternion.Euler(fidget, 0f, 0f);
        }
    }

    /// <summary>
    /// Call this when the character is resized externally.
    /// </summary>
    public void SetBaseScale(float scale)
    {
        baseScale = scale;
    }
}
