using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fixes the VRM T-pose by setting a natural relaxed idle pose on load.
/// Arms down at sides, slight elbow bend, relaxed hands.
/// Attached automatically by CharacterManager.
/// </summary>
public class PoseManager : MonoBehaviour
{
    private Animator animator;
    private Dictionary<HumanBodyBones, Quaternion> restPose = new Dictionary<HumanBodyBones, Quaternion>();

    // The bones we modify for the rest pose
    private static readonly HumanBodyBones[] poseBones = new HumanBodyBones[]
    {
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.Spine,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
    };

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Debug.LogWarning("PoseManager: No humanoid Animator found.");
            enabled = false;
            return;
        }

        ApplyRestPose();
    }

    /// <summary>
    /// Set a natural relaxed pose — arms down, slight bends, no T-pose.
    /// </summary>
    public void ApplyRestPose()
    {
        if (animator == null) return;



        // --- Shoulders: slight downward drop ---
        SetBoneRotation(HumanBodyBones.LeftShoulder, new Vector3(0f, 0f, -5f));
        SetBoneRotation(HumanBodyBones.RightShoulder, new Vector3(0f, 0f, 5f));

        // --- Upper arms: closer to torso ---
        // Left arm: rotate forward and down, closer to body
        SetBoneRotation(HumanBodyBones.LeftUpperArm, new Vector3(10f, 0f, 75f));
        // Right arm: mirror
        SetBoneRotation(HumanBodyBones.RightUpperArm, new Vector3(10f, 0f, -75f));

        // --- Lower arms: slight bend at elbow ---
        SetBoneRotation(HumanBodyBones.LeftLowerArm, new Vector3(-15f, 0f, 0f));
        SetBoneRotation(HumanBodyBones.RightLowerArm, new Vector3(-15f, 0f, 0f));

        // --- Hands: relaxed, slightly curled inward ---
        SetBoneRotation(HumanBodyBones.LeftHand, new Vector3(0f, 0f, -5f));
        SetBoneRotation(HumanBodyBones.RightHand, new Vector3(0f, 0f, 5f));

        // --- Spine: very slight forward lean for natural stance ---
        SetBoneRotation(HumanBodyBones.Spine, new Vector3(2f, 0f, 0f));

        // --- Head: neutral, very slight tilt ---
        SetBoneRotation(HumanBodyBones.Head, new Vector3(-2f, 0f, 0f));

        // --- Legs: very slight natural bend ---
        SetBoneRotation(HumanBodyBones.LeftUpperLeg, new Vector3(2f, 0f, -1f));
        SetBoneRotation(HumanBodyBones.RightUpperLeg, new Vector3(2f, 0f, 1f));
        SetBoneRotation(HumanBodyBones.LeftLowerLeg, new Vector3(-3f, 0f, 0f));
        SetBoneRotation(HumanBodyBones.RightLowerLeg, new Vector3(-3f, 0f, 0f));

        // Save the rest pose so we can return to it
        SaveCurrentAsRestPose();
    }

    /// <summary>
    /// Save all current bone rotations as the rest pose.
    /// </summary>
    public void SaveCurrentAsRestPose()
    {
        restPose.Clear();
        foreach (var bone in poseBones)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t != null)
                restPose[bone] = t.localRotation;
        }
    }

    /// <summary>
    /// Reset all bones back to the saved rest pose.
    /// Called after emote bone animations finish.
    /// </summary>
    public void ResetToRestPose()
    {
        foreach (var kvp in restPose)
        {
            Transform t = animator.GetBoneTransform(kvp.Key);
            if (t != null)
                t.localRotation = kvp.Value;
        }
    }

    /// <summary>
    /// Smoothly blend all bones back to the saved rest pose over the given duration.
    /// Call with StartCoroutine from another MonoBehaviour.
    /// </summary>
    /// <summary> Perlin's SmootherStep — zero velocity AND acceleration at boundaries. </summary>
    private static float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    public IEnumerator SmoothResetToRestPose(float duration)
    {
        if (animator == null || restPose.Count == 0) yield break;

        // Snapshot current rotations
        Dictionary<HumanBodyBones, Quaternion> startRotations = new Dictionary<HumanBodyBones, Quaternion>();
        foreach (var kvp in restPose)
        {
            Transform t = animator.GetBoneTransform(kvp.Key);
            if (t != null)
                startRotations[kvp.Key] = t.localRotation;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float blend = SmootherStep(elapsed / duration);
            foreach (var kvp in restPose)
            {
                Transform t = animator.GetBoneTransform(kvp.Key);
                if (t != null && startRotations.ContainsKey(kvp.Key))
                    t.localRotation = Quaternion.Slerp(startRotations[kvp.Key], kvp.Value, blend);
            }
            yield return null;
        }

        // Ensure final values are exact
        ResetToRestPose();
    }

    /// <summary>
    /// Get a bone transform by HumanBodyBones enum.
    /// </summary>
    public Transform GetBone(HumanBodyBones bone)
    {
        if (animator == null) return null;
        return animator.GetBoneTransform(bone);
    }

    /// <summary>
    /// Get the saved rest rotation for a bone.
    /// </summary>
    public Quaternion GetRestRotation(HumanBodyBones bone)
    {
        if (restPose.ContainsKey(bone))
            return restPose[bone];
        Transform t = animator?.GetBoneTransform(bone);
        return t != null ? t.localRotation : Quaternion.identity;
    }

    // --- Internal ---

    private void SetBoneRotation(HumanBodyBones bone, Vector3 eulerOffset)
    {
        Transform t = animator.GetBoneTransform(bone);
        if (t != null)
        {
            t.localRotation = t.localRotation * Quaternion.Euler(eulerOffset);
        }
    }
}
