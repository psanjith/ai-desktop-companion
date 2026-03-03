using UnityEngine;

/// <summary>
/// Organic idle animation using layered multi-frequency oscillation.
/// Each bone uses 3 overlapping sine waves at irrational frequency ratios
/// so the motion pattern never exactly repeats — creating natural, humanlike movement.
/// Bones chase their targets via exponential smoothing for weight and follow-through.
/// </summary>
public class IdleAnimator : MonoBehaviour
{
    [Header("Bobbing (up/down)")]
    public float bobAmount = 0.012f;
    public float bobSpeed = 0.55f;

    [Header("Breathing (chest scale pulse)")]
    public float breatheAmount = 0.003f;
    public float breatheSpeed = 0.75f;

    [Header("Body Sway")]
    public float swayAmount = 1.2f;
    public float swaySpeed = 0.25f;

    [Header("Weight Shift (hips)")]
    public float weightShiftAmount = 0.9f;
    public float weightShiftSpeed = 0.15f;

    [Header("Head Movement")]
    public float headTiltAmount = 2.0f;
    public float headTiltSpeed = 0.14f;
    public float headTurnAmount = 3.0f;
    public float headTurnSpeed = 0.09f;

    [Header("Arm Micro-Movement")]
    public float armSwayAmount = 0.8f;
    public float armSwaySpeed = 0.28f;

    [Header("Elbow Movement")]
    public float elbowFlexAmount = 1.5f;
    public float elbowFlexSpeed = 0.20f;

    [Header("Smoothing")]
    public float boneSmoothing = 5f; // how fast bones chase their target (higher = snappier)

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
    private float blendWeight = 1f;
    private float blendSpeed = 5f;

    // Random phase offset so two characters don't move in sync
    private float phaseOffset;

    // Per-bone random seeds for asymmetric motion
    private float[] boneSeeds;

    void Start()
    {
        startPos = transform.localPosition;
        baseScale = transform.localScale.x;
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);

        // Each bone gets a unique random seed for asymmetric, individual motion
        boneSeeds = new float[12];
        for (int i = 0; i < boneSeeds.Length; i++)
            boneSeeds[i] = Random.Range(0f, 100f);

        Invoke(nameof(CacheBones), 0.1f);
    }

    /// <summary>
    /// Organic oscillation: 3 layered sines at irrational frequency ratios.
    /// Creates complex, non-repeating motion that looks natural.
    /// seed: per-bone offset for unique timing.
    /// </summary>
    private float Organic(float t, float speed, float amplitude, float seed)
    {
        float s = t + seed;
        return (Mathf.Sin(s * speed) * 0.55f
              + Mathf.Sin(s * speed * 1.618f + 0.7f) * 0.28f    // golden ratio
              + Mathf.Sin(s * speed * 2.847f + 2.1f) * 0.17f)   // irrational
              * amplitude;
    }

    /// <summary> Same as Organic but with a secondary axis offset for 2D motion. </summary>
    private float Organic2(float t, float speed, float amplitude, float seed)
    {
        float s = t + seed;
        return (Mathf.Sin(s * speed * 0.93f + 1.3f) * 0.5f
              + Mathf.Sin(s * speed * 1.414f + 3.7f) * 0.3f     // sqrt(2)
              + Mathf.Sin(s * speed * 2.236f + 0.4f) * 0.2f)    // sqrt(5)
              * amplitude;
    }

    /// <summary>
    /// Perlin-noise drift — genuinely aperiodic with smooth derivatives.
    /// Feels less "mechanical" than sines even at irrational ratios.
    /// Centered on zero: output range is [-amplitude, +amplitude].
    /// </summary>
    private static float Perlin(float t, float speed, float amplitude, float seed)
    {
        return (Mathf.PerlinNoise(seed * 0.1f + t * speed * 0.1f, seed * 0.37f) - 0.5f) * 2f * amplitude;
    }

    void CacheBones()
    {
        anim = GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman) return;

        headBone      = anim.GetBoneTransform(HumanBodyBones.Head);
        spineBone     = anim.GetBoneTransform(HumanBodyBones.Spine);
        hipsBone      = anim.GetBoneTransform(HumanBodyBones.Hips);
        leftUpperArm  = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightUpperArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        leftLowerArm  = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        rightLowerArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        leftHand      = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand     = anim.GetBoneTransform(HumanBodyBones.RightHand);

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

    /// <summary>
    /// Smoothly chase a target rotation using exponential smoothing.
    /// Adds natural weight/inertia — bones feel like they have mass.
    /// </summary>
    private Quaternion ChaseTarget(Quaternion current, Quaternion target, float smoothing)
    {
        return Quaternion.Slerp(current, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
    }

    void Update()
    {
        float t = Time.time + phaseOffset;
        float dt = Time.deltaTime;

        // ---- Whole-body: organic bob + breathe ----
        float bob = Organic(t, bobSpeed, bobAmount, boneSeeds == null ? 0f : boneSeeds[0]);
        transform.localPosition = startPos + new Vector3(0f, bob, 0f);

        float breathe = Organic(t, breatheSpeed, breatheAmount, boneSeeds == null ? 0f : boneSeeds[1]);
        float s = baseScale + breathe;
        transform.localScale = new Vector3(s, s, s);

        if (!bonesReady || boneSeeds == null) return;

        // Blend weight — exponential decay for organic transitions
        float targetWeight = paused ? 0f : 1f;
        blendWeight = Mathf.Lerp(blendWeight, targetWeight, 1f - Mathf.Exp(-blendSpeed * dt));
        if (Mathf.Abs(blendWeight - targetWeight) < 0.005f) blendWeight = targetWeight;
        if (blendWeight < 0.001f) return;

        // Effective smoothing rate — scaled by blend weight for graceful fade
        float smooth = boneSmoothing * blendWeight;

        // Per-bone smoothing variation — heavier body parts lag more, lighter snap faster
        float headSmooth  = smooth * 1.3f;   // head is light and reactive
        float spineSmooth = smooth * 0.9f;   // spine has moderate mass
        float hipsSmooth  = smooth * 0.55f;  // hips are heavy, shift slowly
        float armSmooth   = smooth * 1.05f;

        // Stillness mask: very slow Perlin drift in [0.55 .. 1.0]
        // Character naturally quiets down and livens up rather than oscillating perfectly
        float stillness = 0.55f + Mathf.PerlinNoise(boneSeeds[11] + t * 0.04f, 0.3f) * 0.45f;

        // Breath phase — reuse the same signal driving chest scale for spine coupling
        float breathPhase = Organic(t, breatheSpeed, 1f, boneSeeds[1]);

        // ---- Spine: organic sway + breath coupling (inhale arches back slightly) ----
        if (spineBone != null)
        {
            float sway   = Organic(t, swaySpeed, swayAmount * stillness, boneSeeds[2]);
            float lean   = Organic2(t, swaySpeed * 0.6f, swayAmount * 0.35f * stillness, boneSeeds[2]);
            float yDrift = Organic(t, swaySpeed * 0.4f, swayAmount * 0.15f, boneSeeds[2] + 5f);
            // Breath coupling: positive breathPhase = inhale, arches spine back ~0.8 degrees
            float breathArch = breathPhase * 0.8f;
            Quaternion target = spineRest * Quaternion.Euler(lean + breathArch, yDrift, sway);
            spineBone.localRotation = ChaseTarget(spineBone.localRotation, target, spineSmooth);
        }

        // ---- Hips: slow weight shifting with subtle 3-axis drift ----
        if (hipsBone != null)
        {
            float yaw   = Organic(t, weightShiftSpeed, weightShiftAmount, boneSeeds[3]);
            float roll  = Organic2(t, weightShiftSpeed * 0.7f, weightShiftAmount * 0.3f, boneSeeds[3]);
            Quaternion target = hipsRest * Quaternion.Euler(0f, yaw, roll);
            hipsBone.localRotation = ChaseTarget(hipsBone.localRotation, target, hipsSmooth);
        }

        // ---- Head: Perlin-based look-around — aperiodic drift with no sine regularity ----
        if (headBone != null)
        {
            // Perlin gives genuinely non-repeating motion; stillness mask makes head
            // occasionally settle for a beat, like a real person losing interest momentarily
            float tilt = Perlin(t, headTiltSpeed, headTiltAmount * stillness, boneSeeds[4]);
            float turn = Perlin(t, headTurnSpeed, headTurnAmount * stillness, boneSeeds[4] + 13f);
            float nod  = Perlin(t, headTiltSpeed * 0.5f, headTiltAmount * 0.4f * stillness, boneSeeds[4] + 7f);
            Quaternion target = headRest * Quaternion.Euler(nod, turn, tilt);
            headBone.localRotation = ChaseTarget(headBone.localRotation, target, headSmooth);
        }

        // ---- Arms: asymmetric sway (left and right feel independent) ----
        if (leftUpperArm != null)
        {
            float sw  = Organic(t, armSwaySpeed, armSwayAmount, boneSeeds[5]);
            float sw2 = Organic2(t, armSwaySpeed * 0.7f, armSwayAmount * 0.25f, boneSeeds[5]);
            Quaternion target = leftArmRest * Quaternion.Euler(sw, sw2 * 0.3f, sw * 0.25f);
            leftUpperArm.localRotation = ChaseTarget(leftUpperArm.localRotation, target, armSmooth);
        }
        if (rightUpperArm != null)
        {
            // Slightly different speed multiplier for natural asymmetry
            float sw  = Organic(t, armSwaySpeed * 1.07f, armSwayAmount, boneSeeds[6]);
            float sw2 = Organic2(t, armSwaySpeed * 0.8f, armSwayAmount * 0.25f, boneSeeds[6]);
            Quaternion target = rightArmRest * Quaternion.Euler(sw, -sw2 * 0.3f, -sw * 0.25f);
            rightUpperArm.localRotation = ChaseTarget(rightUpperArm.localRotation, target, armSmooth);
        }

        // ---- Lower arms: organic flex with twist ----
        if (leftLowerArm != null)
        {
            float flex  = Organic(t, elbowFlexSpeed, elbowFlexAmount, boneSeeds[7]);
            float twist = Organic2(t, elbowFlexSpeed * 0.6f, elbowFlexAmount * 0.25f, boneSeeds[7]);
            Quaternion target = leftLowerArmRest * Quaternion.Euler(flex, twist, 0f);
            leftLowerArm.localRotation = ChaseTarget(leftLowerArm.localRotation, target, armSmooth);
        }
        if (rightLowerArm != null)
        {
            float flex  = Organic(t, elbowFlexSpeed * 1.05f, elbowFlexAmount, boneSeeds[8]);
            float twist = Organic2(t, elbowFlexSpeed * 0.65f, elbowFlexAmount * 0.25f, boneSeeds[8]);
            Quaternion target = rightLowerArmRest * Quaternion.Euler(flex, twist, 0f);
            rightLowerArm.localRotation = ChaseTarget(rightLowerArm.localRotation, target, armSmooth);
        }

        // ---- Hands: subtle organic fidget on multiple axes ----
        if (leftHand != null)
        {
            float fx = Organic(t, 0.7f, 1.2f, boneSeeds[9]);
            float fy = Organic2(t, 0.5f, 0.8f, boneSeeds[9]);
            Quaternion target = leftHandRest * Quaternion.Euler(fx, fy, 0f);
            leftHand.localRotation = ChaseTarget(leftHand.localRotation, target, smooth);
        }
        if (rightHand != null)
        {
            float fx = Organic(t, 0.65f, 1.2f, boneSeeds[10]);
            float fy = Organic2(t, 0.55f, 0.8f, boneSeeds[10]);
            Quaternion target = rightHandRest * Quaternion.Euler(fx, -fy, 0f);
            rightHand.localRotation = ChaseTarget(rightHand.localRotation, target, smooth);
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
