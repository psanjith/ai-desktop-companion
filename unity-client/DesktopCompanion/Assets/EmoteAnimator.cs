using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays procedural emote animations on the character.
/// Emote keywords from the LLM are mapped to transform-based + bone animations.
/// Uses PoseManager for bone access when available.
/// </summary>
public class EmoteAnimator : MonoBehaviour
{
    private bool isPlaying = false;
    private Vector3 originalPos;
    private Quaternion originalRot;
    private Vector3 originalScale;
    private PoseManager poseManager;

    void Start()
    {
        CaptureOriginalState();
        poseManager = GetComponent<PoseManager>();
    }

    public void CaptureOriginalState()
    {
        originalPos = transform.localPosition;
        originalRot = transform.localRotation;
        originalScale = transform.localScale;
    }

    /// <summary>
    /// Trigger an emote animation by keyword.
    /// </summary>
    public void PlayEmote(string emote)
    {
        if (isPlaying) return; // Don't stack animations

        string lower = emote.ToLower().Trim();

        // Map keywords to animations
        if (lower.Contains("yawn"))
            StartCoroutine(DoTiltBack());
        else if (lower.Contains("nod"))
            StartCoroutine(DoNod());
        else if (lower.Contains("wave") || lower.Contains("greet") || lower.Contains("hi"))
            StartCoroutine(DoWave());
        else if (lower.Contains("giggle") || lower.Contains("laugh") || lower.Contains("chuckle"))
            StartCoroutine(DoBounce());
        else if (lower.Contains("sigh"))
            StartCoroutine(DoDeflate());
        else if (lower.Contains("stretch"))
            StartCoroutine(DoStretch());
        else if (lower.Contains("jump") || lower.Contains("excited") || lower.Contains("happy"))
            StartCoroutine(DoJump());
        else if (lower.Contains("think") || lower.Contains("ponder") || lower.Contains("hmm"))
            StartCoroutine(DoTiltSide());
        else if (lower.Contains("blush") || lower.Contains("shy") || lower.Contains("embarrass"))
            StartCoroutine(DoWiggle());
        else if (lower.Contains("blink") || lower.Contains("stare"))
            StartCoroutine(DoSquash());
        else if (lower.Contains("shrug"))
            StartCoroutine(DoShrug());
        else if (lower.Contains("shake") || lower.Contains("no") || lower.Contains("disagree"))
            StartCoroutine(DoHeadShake());
        else if (lower.Contains("spin") || lower.Contains("twirl"))
            StartCoroutine(DoSpin());
        else if (lower.Contains("dance") || lower.Contains("groove") || lower.Contains("sway"))
            StartCoroutine(DoDance());
        else if (lower.Contains("peek") || lower.Contains("hide") || lower.Contains("sneak"))
            StartCoroutine(DoPeek());
        else if (lower.Contains("pout") || lower.Contains("sulk") || lower.Contains("grumpy"))
            StartCoroutine(DoPout());
        else if (lower.Contains("clap") || lower.Contains("applaud") || lower.Contains("bravo"))
            StartCoroutine(DoClap());
        else if (lower.Contains("gasp") || lower.Contains("shock") || lower.Contains("surprise"))
            StartCoroutine(DoGasp());
        else if (lower.Contains("pat") || lower.Contains("comfort") || lower.Contains("hug"))
            StartCoroutine(DoGentle());
        else
            StartCoroutine(DoBounce()); // Default: gentle bounce

        // Also trigger facial expression if FaceAnimator exists
        var faceAnim = GetComponent<FaceAnimator>();
        if (faceAnim != null)
            faceAnim.SetEmotionFromEmote(lower);
    }

    // --- Emote Animations ---

    /// <summary> Tilt head back (yawn) </summary>
    IEnumerator DoTiltBack()
    {
        isPlaying = true;
        float duration = 1.2f;
        float maxAngle = 15f;

        // Tilt back
        yield return RotateOverTime(Vector3.right, maxAngle, duration * 0.4f);
        yield return new WaitForSeconds(duration * 0.2f);
        // Return
        yield return RotateOverTime(Vector3.right, -maxAngle, duration * 0.4f);

        ResetState();
    }

    /// <summary> Quick nods using head bone </summary>
    IEnumerator DoNod()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (head != null)
        {
            Quaternion rest = head.localRotation;
            for (int i = 0; i < 3; i++)
            {
                Quaternion down = rest * Quaternion.Euler(12f, 0f, 0f);
                yield return RotateBoneOverTime(head, head.localRotation, down, 0.12f);
                yield return RotateBoneOverTime(head, down, rest, 0.12f);
            }
        }
        else
        {
            float nodAngle = 10f;
            float speed = 0.15f;
            for (int i = 0; i < 3; i++)
            {
                yield return RotateOverTime(Vector3.right, nodAngle, speed);
                yield return RotateOverTime(Vector3.right, -nodAngle, speed);
            }
        }

        ResetState();
    }

    /// <summary> Wave with actual arm bone + body sway </summary>
    IEnumerator DoWave()
    {
        isPlaying = true;
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        if (rightArm != null)
        {
            // Raise right arm up
            Quaternion armRest = rightArm.localRotation;
            Quaternion armRaised = armRest * Quaternion.Euler(-60f, 0f, -30f);
            Quaternion lowerRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion lowerBent = lowerRest * Quaternion.Euler(-40f, 0f, 0f);

            yield return RotateBoneOverTime(rightArm, armRest, armRaised, 0.25f);
            if (rightLower != null)
                yield return RotateBoneOverTime(rightLower, lowerRest, lowerBent, 0.15f);

            // Wave back and forth
            for (int i = 0; i < 3; i++)
            {
                Quaternion waveA = armRaised * Quaternion.Euler(0f, 0f, 15f);
                Quaternion waveB = armRaised * Quaternion.Euler(0f, 0f, -15f);
                yield return RotateBoneOverTime(rightArm, rightArm.localRotation, waveA, 0.12f);
                yield return RotateBoneOverTime(rightArm, waveA, waveB, 0.24f);
                yield return RotateBoneOverTime(rightArm, waveB, armRaised, 0.12f);
            }

            // Lower arm back
            if (rightLower != null)
                yield return RotateBoneOverTime(rightLower, rightLower.localRotation, lowerRest, 0.15f);
            yield return RotateBoneOverTime(rightArm, rightArm.localRotation, armRest, 0.25f);
        }
        else
        {
            // Fallback: body sway
            float angle = 12f;
            float speed = 0.2f;
            for (int i = 0; i < 3; i++)
            {
                yield return RotateOverTime(Vector3.forward, angle, speed);
                yield return RotateOverTime(Vector3.forward, -angle * 2f, speed * 2f);
                yield return RotateOverTime(Vector3.forward, angle, speed);
            }
        }

        ResetState();
    }

    /// <summary> Bouncy shake (giggle/laugh) </summary>
    IEnumerator DoBounce()
    {
        isPlaying = true;
        float bounceHeight = 0.06f;
        float speed = 0.08f;

        for (int i = 0; i < 4; i++)
        {
            yield return MoveOverTime(Vector3.up, bounceHeight, speed);
            yield return MoveOverTime(Vector3.up, -bounceHeight, speed);
        }

        ResetState();
    }

    /// <summary> Deflate and reinflate (sigh) </summary>
    IEnumerator DoDeflate()
    {
        isPlaying = true;
        float shrink = 0.9f;
        float duration = 0.5f;

        yield return ScaleOverTime(originalScale * shrink, duration);
        yield return new WaitForSeconds(0.3f);
        yield return ScaleOverTime(originalScale, duration);

        ResetState();
    }

    /// <summary> Scale up then back (stretch) </summary>
    IEnumerator DoStretch()
    {
        isPlaying = true;
        Vector3 tallScale = new Vector3(originalScale.x * 0.95f, originalScale.y * 1.15f, originalScale.z * 0.95f);

        yield return ScaleOverTime(tallScale, 0.4f);
        yield return new WaitForSeconds(0.5f);
        yield return ScaleOverTime(originalScale, 0.4f);

        ResetState();
    }

    /// <summary> Quick hop up (jump/excited) </summary>
    IEnumerator DoJump()
    {
        isPlaying = true;
        float jumpHeight = 0.15f;
        float upSpeed = 0.12f;
        float downSpeed = 0.1f;

        for (int i = 0; i < 2; i++)
        {
            yield return MoveOverTime(Vector3.up, jumpHeight, upSpeed);
            yield return MoveOverTime(Vector3.up, -jumpHeight, downSpeed);
        }

        ResetState();
    }

    /// <summary> Head tilt to side using bone (thinking) </summary>
    IEnumerator DoTiltSide()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (head != null)
        {
            Quaternion rest = head.localRotation;
            Quaternion tilted = rest * Quaternion.Euler(0f, 0f, 15f);
            yield return RotateBoneOverTime(head, rest, tilted, 0.3f);
            yield return new WaitForSeconds(0.8f);
            yield return RotateBoneOverTime(head, tilted, rest, 0.3f);
        }
        else
        {
            float angle = 12f;
            yield return RotateOverTime(Vector3.forward, angle, 0.3f);
            yield return new WaitForSeconds(0.8f);
            yield return RotateOverTime(Vector3.forward, -angle, 0.3f);
        }

        ResetState();
    }

    /// <summary> Quick side-to-side wiggle (blush/shy) </summary>
    IEnumerator DoWiggle()
    {
        isPlaying = true;
        float amount = 0.02f;
        float speed = 0.05f;

        for (int i = 0; i < 5; i++)
        {
            yield return MoveOverTime(Vector3.right, amount, speed);
            yield return MoveOverTime(Vector3.right, -amount * 2f, speed * 2f);
            yield return MoveOverTime(Vector3.right, amount, speed);
        }

        ResetState();
    }

    /// <summary> Quick squash and stretch (blink) </summary>
    IEnumerator DoSquash()
    {
        isPlaying = true;
        Vector3 squash = new Vector3(originalScale.x * 1.05f, originalScale.y * 0.85f, originalScale.z * 1.05f);

        yield return ScaleOverTime(squash, 0.1f);
        yield return ScaleOverTime(originalScale, 0.15f);

        ResetState();
    }

    /// <summary> Shoulders up/down (shrug) </summary>
    IEnumerator DoShrug()
    {
        isPlaying = true;
        float upAmount = 0.04f;

        yield return MoveOverTime(Vector3.up, upAmount, 0.15f);
        yield return new WaitForSeconds(0.4f);
        yield return MoveOverTime(Vector3.up, -upAmount, 0.2f);

        ResetState();
    }

    /// <summary> Head shake side to side using bone (no/disagree) </summary>
    IEnumerator DoHeadShake()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (head != null)
        {
            Quaternion rest = head.localRotation;
            for (int i = 0; i < 3; i++)
            {
                Quaternion left = rest * Quaternion.Euler(0f, -15f, 0f);
                Quaternion right = rest * Quaternion.Euler(0f, 15f, 0f);
                yield return RotateBoneOverTime(head, head.localRotation, left, 0.1f);
                yield return RotateBoneOverTime(head, left, right, 0.2f);
                yield return RotateBoneOverTime(head, right, rest, 0.1f);
            }
        }
        else
        {
            float angle = 10f;
            float speed = 0.1f;
            for (int i = 0; i < 3; i++)
            {
                yield return RotateOverTime(Vector3.up, angle, speed);
                yield return RotateOverTime(Vector3.up, -angle * 2f, speed * 2f);
                yield return RotateOverTime(Vector3.up, angle, speed);
            }
        }

        ResetState();
    }

    // --- New Emote Animations ---

    /// <summary> Full 360 spin (twirl/spin) </summary>
    IEnumerator DoSpin()
    {
        isPlaying = true;
        float duration = 0.6f;
        float elapsed = 0f;
        Quaternion startRot = transform.localRotation;

        // Slight hop + spin
        Vector3 startPos = transform.localPosition;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float angle = Mathf.Lerp(0f, 360f, t);
            transform.localRotation = startRot * Quaternion.Euler(0f, angle, 0f);
            // Little hop arc
            float hop = Mathf.Sin(t * Mathf.PI) * 0.08f;
            transform.localPosition = startPos + Vector3.up * hop;
            yield return null;
        }

        ResetState();
    }

    /// <summary> Side-to-side dance sway with bounce </summary>
    IEnumerator DoDance()
    {
        isPlaying = true;
        float swayAngle = 10f;
        float bounceHeight = 0.04f;
        float beatTime = 0.2f;

        for (int i = 0; i < 6; i++)
        {
            float dir = (i % 2 == 0) ? 1f : -1f;
            float elapsed = 0f;
            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            Quaternion targetRot = originalRot * Quaternion.Euler(0f, 0f, swayAngle * dir);

            while (elapsed < beatTime)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / beatTime;
                transform.localRotation = Quaternion.Slerp(startRot, targetRot, Mathf.SmoothStep(0, 1, t));
                float bounce = Mathf.Sin(t * Mathf.PI) * bounceHeight;
                transform.localPosition = originalPos + Vector3.up * bounce;
                yield return null;
            }
        }

        ResetState();
    }

    /// <summary> Peek out from below (shy peek) </summary>
    IEnumerator DoPeek()
    {
        isPlaying = true;
        float hideAmount = -0.12f;

        // Duck down
        yield return MoveOverTime(Vector3.up, hideAmount, 0.2f);
        yield return new WaitForSeconds(0.3f);
        // Peek up slowly
        yield return MoveOverTime(Vector3.up, -hideAmount * 0.7f, 0.4f);
        yield return new WaitForSeconds(0.5f);
        // Go back to normal
        yield return MoveOverTime(Vector3.up, -hideAmount * 0.3f + hideAmount, 0.2f);

        ResetState();
    }

    /// <summary> Puff up and deflate (pout/sulk) </summary>
    IEnumerator DoPout()
    {
        isPlaying = true;
        Vector3 puffScale = new Vector3(originalScale.x * 1.08f, originalScale.y * 0.95f, originalScale.z * 1.08f);

        // Puff up
        yield return ScaleOverTime(puffScale, 0.2f);
        // Tiny head shakes while pouting
        for (int i = 0; i < 2; i++)
        {
            yield return RotateOverTime(Vector3.forward, 5f, 0.1f);
            yield return RotateOverTime(Vector3.forward, -10f, 0.2f);
            yield return RotateOverTime(Vector3.forward, 5f, 0.1f);
        }
        yield return new WaitForSeconds(0.2f);
        // Deflate back
        yield return ScaleOverTime(originalScale, 0.3f);

        ResetState();
    }

    /// <summary> Quick bouncy clap </summary>
    IEnumerator DoClap()
    {
        isPlaying = true;
        float squishAmount = 0.04f;
        float speed = 0.06f;

        for (int i = 0; i < 5; i++)
        {
            // Squish in (hands coming together)
            Vector3 squished = new Vector3(originalScale.x * 0.92f, originalScale.y * 1.03f, originalScale.z);
            yield return ScaleOverTime(squished, speed);
            yield return ScaleOverTime(originalScale, speed);
            // Tiny bounce with each clap
            yield return MoveOverTime(Vector3.up, squishAmount, speed * 0.5f);
            yield return MoveOverTime(Vector3.up, -squishAmount, speed * 0.5f);
        }

        ResetState();
    }

    /// <summary> Jolt back in surprise (gasp) </summary>
    IEnumerator DoGasp()
    {
        isPlaying = true;

        // Quick jolt backward + up
        Vector3 startPos = transform.localPosition;
        Vector3 joltPos = startPos + new Vector3(0f, 0.06f, -0.03f);
        Vector3 tallScale = new Vector3(originalScale.x * 0.9f, originalScale.y * 1.12f, originalScale.z * 0.9f);

        yield return ScaleOverTime(tallScale, 0.08f);
        transform.localPosition = joltPos;
        yield return new WaitForSeconds(0.5f);

        // Settle back
        yield return ScaleOverTime(originalScale, 0.3f);
        float elapsed = 0f;
        while (elapsed < 0.3f)
        {
            elapsed += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(joltPos, originalPos, Mathf.SmoothStep(0, 1, elapsed / 0.3f));
            yield return null;
        }

        ResetState();
    }

    /// <summary> Gentle forward lean (comfort/pat/hug) </summary>
    IEnumerator DoGentle()
    {
        isPlaying = true;

        // Lean forward gently
        yield return RotateOverTime(Vector3.right, 8f, 0.3f);
        yield return new WaitForSeconds(0.8f);
        // Back to normal
        yield return RotateOverTime(Vector3.right, -8f, 0.3f);

        ResetState();
    }

    // --- Helper coroutines ---

    IEnumerator RotateOverTime(Vector3 axis, float angle, float duration)
    {
        float elapsed = 0f;
        Quaternion startRot = transform.localRotation;
        Quaternion endRot = transform.localRotation * Quaternion.AngleAxis(angle, axis);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.localRotation = Quaternion.Slerp(startRot, endRot, t);
            yield return null;
        }
        transform.localRotation = endRot;
    }

    IEnumerator MoveOverTime(Vector3 direction, float distance, float duration)
    {
        float elapsed = 0f;
        Vector3 startPos = transform.localPosition;
        Vector3 endPos = startPos + direction * distance;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.localPosition = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }
        transform.localPosition = endPos;
    }

    IEnumerator ScaleOverTime(Vector3 targetScale, float duration)
    {
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }
        transform.localScale = targetScale;
    }

    /// <summary> Smoothly rotate a specific bone from one rotation to another. </summary>
    IEnumerator RotateBoneOverTime(Transform bone, Quaternion from, Quaternion to, float duration)
    {
        if (bone == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            bone.localRotation = Quaternion.Slerp(from, to, t);
            yield return null;
        }
        bone.localRotation = to;
    }

    private void ResetState()
    {
        transform.localPosition = originalPos;
        transform.localRotation = originalRot;
        transform.localScale = originalScale;

        // Reset any bone animations back to the rest pose
        if (poseManager != null)
            poseManager.ResetToRestPose();

        isPlaying = false;
    }
}
