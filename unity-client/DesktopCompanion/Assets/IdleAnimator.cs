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
    public float bobAmount = 0.010f;
    public float bobSpeed = 0.45f;

    [Header("Breathing (chest scale pulse)")]
    public float breatheAmount = 0.002f;
    public float breatheSpeed = 0.65f;

    [Header("Body Sway")]
    public float swayAmount = 1.0f;
    public float swaySpeed = 0.20f;

    [Header("Weight Shift (hips)")]
    public float weightShiftAmount = 0.7f;
    public float weightShiftSpeed = 0.12f;

    [Header("Head Movement")]
    public float headTiltAmount = 1.8f;
    public float headTiltSpeed = 0.11f;
    public float headTurnAmount = 2.5f;
    public float headTurnSpeed = 0.07f;

    [Header("Arm Micro-Movement")]
    public float armSwayAmount = 0.7f;
    public float armSwaySpeed = 0.22f;

    [Header("Elbow Movement")]
    public float elbowFlexAmount = 1.0f;
    public float elbowFlexSpeed = 0.16f;

    [Header("Smoothing")]
    public float boneSmoothing = 3.5f; // how fast bones chase their target (higher = snappier)

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

    // Mood-reactive amplitude/speed multipliers — smoothly transition on emotion change
    private float _targetAmpMult   = 1f;
    private float _targetSpeedMult = 1f;
    private float _curAmpMult      = 1f;
    private float _curSpeedMult    = 1f;

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

        // Smoothly transition mood multipliers (~4s full transition at exp rate 0.4/s)
        _curAmpMult   = Mathf.Lerp(_curAmpMult,   _targetAmpMult,   1f - Mathf.Exp(-0.4f * dt));
        _curSpeedMult = Mathf.Lerp(_curSpeedMult, _targetSpeedMult, 1f - Mathf.Exp(-0.4f * dt));

        // ---- Whole-body: organic bob + breathe ----
        float bob = Organic(t, bobSpeed * _curSpeedMult, bobAmount * _curAmpMult, boneSeeds == null ? 0f : boneSeeds[0]);
        transform.localPosition = startPos + new Vector3(0f, bob, 0f);

        float breathe = Organic(t, breatheSpeed * _curSpeedMult, breatheAmount * _curAmpMult, boneSeeds == null ? 0f : boneSeeds[1]);
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

        // Stillness mask: very slow Perlin drift in [0.20 .. 0.88]
        // Lower floor means the character genuinely settles to near-motionless periodically,
        // like a real person who isn't constantly fidgeting.
        float stillness = 0.20f + Mathf.PerlinNoise(boneSeeds[11] + t * 0.035f, 0.3f) * 0.68f;

        // Breath phase — reuse the same signal driving chest scale for spine coupling
        float breathPhase = Organic(t, breatheSpeed, 1f, boneSeeds[1]);

        // ---- Spine: organic sway + breath coupling (inhale arches back slightly) ----
        if (spineBone != null)
        {
            float sway   = Organic(t, swaySpeed * _curSpeedMult, swayAmount * stillness * _curAmpMult, boneSeeds[2]);
            float lean   = Organic2(t, swaySpeed * 0.6f * _curSpeedMult, swayAmount * 0.35f * stillness * _curAmpMult, boneSeeds[2]);
            float yDrift = Organic(t, swaySpeed * 0.4f * _curSpeedMult, swayAmount * 0.15f * _curAmpMult, boneSeeds[2] + 5f);
            // Breath coupling: positive breathPhase = inhale, arches spine back ~0.8 degrees
            float breathArch = breathPhase * 0.8f;
            Quaternion target = spineRest * Quaternion.Euler(lean + breathArch, yDrift, sway);
            spineBone.localRotation = ChaseTarget(spineBone.localRotation, target, spineSmooth);
        }

        // ---- Hips: slow weight shifting with subtle 3-axis drift ----
        if (hipsBone != null)
        {
            float yaw   = Organic(t, weightShiftSpeed * _curSpeedMult, weightShiftAmount * _curAmpMult, boneSeeds[3]);
            float roll  = Organic2(t, weightShiftSpeed * 0.7f * _curSpeedMult, weightShiftAmount * 0.3f * _curAmpMult, boneSeeds[3]);
            Quaternion target = hipsRest * Quaternion.Euler(0f, yaw, roll);
            hipsBone.localRotation = ChaseTarget(hipsBone.localRotation, target, hipsSmooth);
        }

        // ---- Head: Perlin-based look-around — aperiodic drift with no sine regularity ----
        if (headBone != null)
        {
            // Perlin gives genuinely non-repeating motion; stillness mask makes head
            // occasionally settle for a beat, like a real person losing interest momentarily
            float tilt = Perlin(t, headTiltSpeed * _curSpeedMult, headTiltAmount * stillness * _curAmpMult, boneSeeds[4]);
            float turn = Perlin(t, headTurnSpeed * _curSpeedMult, headTurnAmount * stillness * _curAmpMult, boneSeeds[4] + 13f);
            float nod  = Perlin(t, headTiltSpeed * 0.5f * _curSpeedMult, headTiltAmount * 0.4f * stillness * _curAmpMult, boneSeeds[4] + 7f);
            Quaternion target = headRest * Quaternion.Euler(nod, turn, tilt);
            headBone.localRotation = ChaseTarget(headBone.localRotation, target, headSmooth);
        }

        // ---- Arms: asymmetric sway (left and right feel independent) ----
        if (leftUpperArm != null)
        {
            float sw  = Organic(t, armSwaySpeed * _curSpeedMult, armSwayAmount * stillness * _curAmpMult, boneSeeds[5]);
            float sw2 = Organic2(t, armSwaySpeed * 0.7f * _curSpeedMult, armSwayAmount * 0.25f * stillness * _curAmpMult, boneSeeds[5]);
            Quaternion target = leftArmRest * Quaternion.Euler(sw, sw2 * 0.3f, sw * 0.25f);
            leftUpperArm.localRotation = ChaseTarget(leftUpperArm.localRotation, target, armSmooth);
        }
        if (rightUpperArm != null)
        {
            // Slightly different speed multiplier for natural asymmetry
            float sw  = Organic(t, armSwaySpeed * 1.07f * _curSpeedMult, armSwayAmount * _curAmpMult, boneSeeds[6]);
            float sw2 = Organic2(t, armSwaySpeed * 0.8f * _curSpeedMult, armSwayAmount * 0.25f * stillness * _curAmpMult, boneSeeds[6]);
            Quaternion target = rightArmRest * Quaternion.Euler(sw, -sw2 * 0.3f, -sw * 0.25f);
            rightUpperArm.localRotation = ChaseTarget(rightUpperArm.localRotation, target, armSmooth);
        }

        // ---- Lower arms: organic flex with twist ----
        if (leftLowerArm != null)
        {
            float flex  = Organic(t, elbowFlexSpeed * _curSpeedMult, elbowFlexAmount * stillness * _curAmpMult, boneSeeds[7]);
            float twist = Organic2(t, elbowFlexSpeed * 0.6f * _curSpeedMult, elbowFlexAmount * 0.25f * stillness * _curAmpMult, boneSeeds[7]);
            Quaternion target = leftLowerArmRest * Quaternion.Euler(flex, twist, 0f);
            leftLowerArm.localRotation = ChaseTarget(leftLowerArm.localRotation, target, armSmooth);
        }
        if (rightLowerArm != null)
        {
            float flex  = Organic(t, elbowFlexSpeed * 1.05f * _curSpeedMult, elbowFlexAmount * stillness * _curAmpMult, boneSeeds[8]);
            float twist = Organic2(t, elbowFlexSpeed * 0.65f * _curSpeedMult, elbowFlexAmount * 0.25f * stillness * _curAmpMult, boneSeeds[8]);
            Quaternion target = rightLowerArmRest * Quaternion.Euler(flex, twist, 0f);
            rightLowerArm.localRotation = ChaseTarget(rightLowerArm.localRotation, target, armSmooth);
        }

        // ---- Hands: subtle organic fidget on multiple axes ----
        if (leftHand != null)
        {
            float fx = Organic(t, 0.7f * _curSpeedMult, 0.7f * stillness * _curAmpMult, boneSeeds[9]);
            float fy = Organic2(t, 0.5f * _curSpeedMult, 0.5f * stillness * _curAmpMult, boneSeeds[9]);
            Quaternion target = leftHandRest * Quaternion.Euler(fx, fy, 0f);
            leftHand.localRotation = ChaseTarget(leftHand.localRotation, target, smooth);
        }
        if (rightHand != null)
        {
            float fx = Organic(t, 0.65f * _curSpeedMult, 0.7f * stillness * _curAmpMult, boneSeeds[10]);
            float fy = Organic2(t, 0.55f * _curSpeedMult, 0.5f * stillness * _curAmpMult, boneSeeds[10]);
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

    /// <summary>
    /// Set the emotional state of the idle animation.
    /// joy=lively, sorrow=subdued, angry=tense, fun=playful, neutral=default.
    /// Transitions smoothly over ~4 seconds to avoid jarring snaps.
    /// </summary>
    public void SetMood(string emotion)
    {
        switch (emotion?.ToLower() ?? "neutral")
        {
            case "joy":    _targetAmpMult = 1.55f; _targetSpeedMult = 1.20f; break;
            case "fun":    _targetAmpMult = 1.30f; _targetSpeedMult = 1.10f; break;
            case "angry":  _targetAmpMult = 1.10f; _targetSpeedMult = 1.30f; break;
            case "sorrow": _targetAmpMult = 0.45f; _targetSpeedMult = 0.70f; break;
            default:       _targetAmpMult = 1.00f; _targetSpeedMult = 1.00f; break;
        }
    }
}
