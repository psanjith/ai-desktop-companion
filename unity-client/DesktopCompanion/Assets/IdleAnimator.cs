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
    public float bobAmount = 0.025f;
    public float bobSpeed = 0.7f;

    [Header("Breathing (chest scale pulse)")]
    public float breatheAmount = 0.005f;
    public float breatheSpeed = 1.2f;

    [Header("Body Sway (side-to-side lean)")]
    public float swayAmount = 2f;       // degrees
    public float swaySpeed = 0.35f;

    [Header("Weight Shift (hips)")]
    public float weightShiftAmount = 2f; // degrees
    public float weightShiftSpeed = 0.22f;

    [Header("Head Movement")]
    public float headTiltAmount = 4f;    // degrees
    public float headTiltSpeed = 0.2f;
    public float headTurnAmount = 6f;    // degrees
    public float headTurnSpeed = 0.14f;

    [Header("Arm Micro-Movement")]
    public float armSwayAmount = 2f;     // degrees
    public float armSwaySpeed = 0.45f;

    [Header("Elbow Movement")]
    public float elbowFlexAmount = 4f;   // degrees
    public float elbowFlexSpeed = 0.3f;

    private Vector3 startPos;
    private float baseScale = 1f;

    // Bone references (cached for performance)
    private Animator anim;
    private Transform headBone;
    private Transform spineBone;
    private Transform hipsBone;
    private Transform leftUpperArm;
    private Transform rightUpperArm;
    private Transform leftLowerArm;
    private Transform rightLowerArm;
    private Transform leftHand;
    private Transform rightHand;

    // Rest rotations (captured after PoseManager sets the natural pose)
    private Quaternion headRest;
    private Quaternion spineRest;
    private Quaternion hipsRest;
    private Quaternion leftArmRest;
    private Quaternion rightArmRest;
    private Quaternion leftLowerArmRest;
    private Quaternion rightLowerArmRest;
    private Quaternion leftHandRest;
    private Quaternion rightHandRest;

    private bool bonesReady = false;

    /// <summary>
    /// When true, idle bone rotations are paused (emote is playing).
    /// Transform-level bob/breathe still runs for subtle aliveness.
    /// </summary>
    public bool paused = false;

    // Smooth blend weight: 0 = paused (bones at current), 1 = fully active idle
    // This prevents snapping when transitioning to/from paused state
    private float blendWeight = 1f;
    private float blendSpeed = 5f; // exponential smoothing rate

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
        leftUpperArm  = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        leftLowerArm  = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        rightLowerArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        leftHand      = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand     = anim.GetBoneTransform(HumanBodyBones.RightHand);

        // Snapshot rest rotations (PoseManager should have applied these already)
        if (headBone != null)       headRest           = headBone.localRotation;
        if (spineBone != null)      spineRest          = spineBone.localRotation;
        if (hipsBone != null)       hipsRest           = hipsBone.localRotation;
        if (leftUpperArm != null)   leftArmRest        = leftUpperArm.localRotation;
        if (rightUpperArm != null)  rightArmRest       = rightUpperArm.localRotation;
        if (leftLowerArm != null)   leftLowerArmRest   = leftLowerArm.localRotation;
        if (rightLowerArm != null)  rightLowerArmRest  = rightLowerArm.localRotation;
        if (leftHand != null)       leftHandRest       = leftHand.localRotation;
        if (rightHand != null)      rightHandRest      = rightHand.localRotation;

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

        if (!bonesReady) return;

        // Smooth blend weight — exponential decay for organic ease-in/ease-out
        float targetWeight = paused ? 0f : 1f;
        blendWeight = Mathf.Lerp(blendWeight, targetWeight, 1f - Mathf.Exp(-blendSpeed * Time.deltaTime));
        if (Mathf.Abs(blendWeight - targetWeight) < 0.005f) blendWeight = targetWeight;

        if (blendWeight < 0.001f) return; // Fully paused, skip bone work

        float w = blendWeight; // shorthand

        // ---- Spine: gentle side sway (shifting weight feel) ----
        if (spineBone != null)
        {
            float sway = Mathf.Sin(t * swaySpeed) * swayAmount;
            float lean = Mathf.Sin(t * swaySpeed * 0.7f) * (swayAmount * 0.3f);
            Quaternion target = spineRest * Quaternion.Euler(lean, 0f, sway);
            spineBone.localRotation = Quaternion.Slerp(spineBone.localRotation, target, w);
        }

        // ---- Hips: subtle rotation for weight shift ----
        if (hipsBone != null)
        {
            float shift = Mathf.Sin(t * weightShiftSpeed) * weightShiftAmount;
            Quaternion target = hipsRest * Quaternion.Euler(0f, shift, 0f);
            hipsBone.localRotation = Quaternion.Slerp(hipsBone.localRotation, target, w);
        }

        // ---- Head: slow look-around (tilt + turn on different phases) ----
        if (headBone != null)
        {
            float tilt = Mathf.Sin(t * headTiltSpeed) * headTiltAmount;
            float turn = Mathf.Sin(t * headTurnSpeed + 1.5f) * headTurnAmount;
            float nod  = Mathf.Sin(t * headTiltSpeed * 0.6f + 0.8f) * (headTiltAmount * 0.5f);
            Quaternion target = headRest * Quaternion.Euler(nod, turn, tilt);
            headBone.localRotation = Quaternion.Slerp(headBone.localRotation, target, w);
        }

        // ---- Arms: gentle micro-sway (natural hanging movement) ----
        if (leftUpperArm != null)
        {
            float armSway = Mathf.Sin(t * armSwaySpeed + 0.5f) * armSwayAmount;
            Quaternion target = leftArmRest * Quaternion.Euler(armSway, 0f, armSway * 0.3f);
            leftUpperArm.localRotation = Quaternion.Slerp(leftUpperArm.localRotation, target, w);
        }
        if (rightUpperArm != null)
        {
            float armSway = Mathf.Sin(t * armSwaySpeed + 2.0f) * armSwayAmount;
            Quaternion target = rightArmRest * Quaternion.Euler(armSway, 0f, -armSway * 0.3f);
            rightUpperArm.localRotation = Quaternion.Slerp(rightUpperArm.localRotation, target, w);
        }

        // ---- Lower arms (elbows): gentle flex/extend ----
        if (leftLowerArm != null)
        {
            float flex = Mathf.Sin(t * elbowFlexSpeed + 2.2f) * elbowFlexAmount;
            float twist = Mathf.Sin(t * elbowFlexSpeed * 0.7f + 1.0f) * (elbowFlexAmount * 0.3f);
            Quaternion target = leftLowerArmRest * Quaternion.Euler(flex, twist, 0f);
            leftLowerArm.localRotation = Quaternion.Slerp(leftLowerArm.localRotation, target, w);
        }
        if (rightLowerArm != null)
        {
            float flex = Mathf.Sin(t * elbowFlexSpeed + 4.0f) * elbowFlexAmount;
            float twist = Mathf.Sin(t * elbowFlexSpeed * 0.7f + 2.8f) * (elbowFlexAmount * 0.3f);
            Quaternion target = rightLowerArmRest * Quaternion.Euler(flex, twist, 0f);
            rightLowerArm.localRotation = Quaternion.Slerp(rightLowerArm.localRotation, target, w);
        }

        // ---- Hands: very subtle fidget ----
        if (leftHand != null)
        {
            float fidget = Mathf.Sin(t * 0.9f + 1f) * 1.5f;
            Quaternion target = leftHandRest * Quaternion.Euler(fidget, 0f, 0f);
            leftHand.localRotation = Quaternion.Slerp(leftHand.localRotation, target, w);
        }
        if (rightHand != null)
        {
            float fidget = Mathf.Sin(t * 0.85f + 3f) * 1.5f;
            Quaternion target = rightHandRest * Quaternion.Euler(fidget, 0f, 0f);
            rightHand.localRotation = Quaternion.Slerp(rightHand.localRotation, target, w);
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
