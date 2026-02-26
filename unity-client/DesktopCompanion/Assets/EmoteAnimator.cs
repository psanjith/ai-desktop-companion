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
        else if (lower.Contains("point") || lower.Contains("look at") || lower.Contains("check"))
            StartCoroutine(DoPoint());
        else if (lower.Contains("facepalm") || lower.Contains("face palm") || lower.Contains("cringe"))
            StartCoroutine(DoFacepalm());
        else if (lower.Contains("look away") || lower.Contains("avoid") || lower.Contains("glance"))
            StartCoroutine(DoLookAway());
        else if (lower.Contains("chin") || lower.Contains("rest") || lower.Contains("hmm"))
            StartCoroutine(DoChinRest());
        else if (lower.Contains("cross") || lower.Contains("fold") || lower.Contains("arms"))
            StartCoroutine(DoCrossArms());
        else if (lower.Contains("raise") || lower.Contains("hands up") || lower.Contains("celebrat"))
            StartCoroutine(DoHandsUp());
        else
            StartCoroutine(DoBounce()); // Default: gentle bounce

        // Also trigger facial expression if FaceAnimator exists
        var faceAnim = GetComponent<FaceAnimator>();
        if (faceAnim != null)
            faceAnim.SetEmotionFromEmote(lower);
    }

    // --- Emote Animations ---

    /// <summary> Tilt head back with arm raise (yawn) — bone-based </summary>
    IEnumerator DoTiltBack()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        if (head != null)
        {
            Quaternion headRest = head.localRotation;
            Quaternion headBack = headRest * Quaternion.Euler(-25f, 0f, 0f);

            // Tilt head back + raise hand to mouth
            Quaternion armRest = Quaternion.identity, armUp = Quaternion.identity;
            Quaternion lowerRest = Quaternion.identity, lowerBent = Quaternion.identity;
            if (rightArm != null)
            {
                armRest = rightArm.localRotation;
                armUp = armRest * Quaternion.Euler(-45f, 0f, -20f);
            }
            if (rightLower != null)
            {
                lowerRest = rightLower.localRotation;
                lowerBent = lowerRest * Quaternion.Euler(-70f, 0f, 0f);
            }

            yield return RotateBoneOverTime(head, headRest, headBack, 0.4f);
            if (rightArm != null) yield return RotateBoneOverTime(rightArm, armRest, armUp, 0.3f);
            if (rightLower != null) yield return RotateBoneOverTime(rightLower, lowerRest, lowerBent, 0.2f);

            yield return new WaitForSeconds(0.5f);

            if (rightLower != null) yield return RotateBoneOverTime(rightLower, lowerBent, lowerRest, 0.2f);
            if (rightArm != null) yield return RotateBoneOverTime(rightArm, armUp, armRest, 0.3f);
            yield return RotateBoneOverTime(head, headBack, headRest, 0.4f);
        }
        else
        {
            float maxAngle = 25f;
            float duration = 1.2f;
            yield return RotateOverTime(Vector3.right, maxAngle, duration * 0.4f);
            yield return new WaitForSeconds(duration * 0.2f);
            yield return RotateOverTime(Vector3.right, -maxAngle, duration * 0.4f);
        }

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
                Quaternion down = rest * Quaternion.Euler(20f, 0f, 0f);
                yield return RotateBoneOverTime(head, head.localRotation, down, 0.12f);
                yield return RotateBoneOverTime(head, down, rest, 0.12f);
            }
        }
        else
        {
            float nodAngle = 15f;
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
            Quaternion armRaised = armRest * Quaternion.Euler(-80f, 0f, -40f);
            Quaternion lowerRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion lowerBent = lowerRest * Quaternion.Euler(-50f, 0f, 0f);

            yield return RotateBoneOverTime(rightArm, armRest, armRaised, 0.25f);
            if (rightLower != null)
                yield return RotateBoneOverTime(rightLower, lowerRest, lowerBent, 0.15f);

            // Wave back and forth
            for (int i = 0; i < 3; i++)
            {
                Quaternion waveA = armRaised * Quaternion.Euler(0f, 0f, 25f);
                Quaternion waveB = armRaised * Quaternion.Euler(0f, 0f, -25f);
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
            float angle = 18f;
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

    /// <summary> Bouncy giggle with head + spine bob (giggle/laugh) — bone-based </summary>
    IEnumerator DoBounce()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        float bounceHeight = 0.10f;
        float speed = 0.08f;

        Quaternion headRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion spineRest = spine != null ? spine.localRotation : Quaternion.identity;

        for (int i = 0; i < 4; i++)
        {
            // Bounce body up
            yield return MoveOverTime(Vector3.up, bounceHeight, speed);
            // Head and spine bob at peak
            if (head != null)
                head.localRotation = headRest * Quaternion.Euler(-10f, (i % 2 == 0 ? 5f : -5f), 0f);
            if (spine != null)
                spine.localRotation = spineRest * Quaternion.Euler(-3f, 0f, (i % 2 == 0 ? 4f : -4f));
            // Bounce back down
            yield return MoveOverTime(Vector3.up, -bounceHeight, speed);
            if (head != null) head.localRotation = headRest;
            if (spine != null) spine.localRotation = spineRest;
        }

        ResetState();
    }

    /// <summary> Deflate and reinflate (sigh) </summary>
    IEnumerator DoDeflate()
    {
        isPlaying = true;
        float shrink = 0.82f;
        float duration = 0.5f;

        yield return ScaleOverTime(originalScale * shrink, duration);
        yield return new WaitForSeconds(0.3f);
        yield return ScaleOverTime(originalScale, duration);

        ResetState();
    }

    /// <summary> Arms-up stretch using bones </summary>
    IEnumerator DoStretch()
    {
        isPlaying = true;
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (leftArm != null && rightArm != null)
        {
            Quaternion lRest = leftArm.localRotation;
            Quaternion rRest = rightArm.localRotation;
            Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
            Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;

            // Arms up + slight back arch
            Quaternion lUp = lRest * Quaternion.Euler(-120f, 0f, 20f);
            Quaternion rUp = rRest * Quaternion.Euler(-120f, 0f, -20f);
            Quaternion sArch = sRest * Quaternion.Euler(-8f, 0f, 0f);
            Quaternion hBack = hRest * Quaternion.Euler(-10f, 0f, 0f);

            // Raise arms
            yield return RotateBoneOverTime(leftArm, lRest, lUp, 0.35f);
            if (rightArm != null) rightArm.localRotation = rUp; // snap other arm
            if (spine != null) yield return RotateBoneOverTime(spine, sRest, sArch, 0.2f);
            if (head != null) head.localRotation = hBack;

            yield return new WaitForSeconds(0.6f);

            // Lower back
            if (head != null) yield return RotateBoneOverTime(head, hBack, hRest, 0.2f);
            if (spine != null) yield return RotateBoneOverTime(spine, sArch, sRest, 0.2f);
            yield return RotateBoneOverTime(leftArm, lUp, lRest, 0.35f);
            if (rightArm != null) rightArm.localRotation = rRest;
        }
        else
        {
            // Fallback: scale stretch
            Vector3 tallScale = new Vector3(originalScale.x * 0.90f, originalScale.y * 1.25f, originalScale.z * 0.90f);
            yield return ScaleOverTime(tallScale, 0.4f);
            yield return new WaitForSeconds(0.5f);
            yield return ScaleOverTime(originalScale, 0.4f);
        }

        ResetState();
    }

    /// <summary> Quick hop up (jump/excited) </summary>
    IEnumerator DoJump()
    {
        isPlaying = true;
        float jumpHeight = 0.22f;
        float upSpeed = 0.10f;
        float downSpeed = 0.08f;

        for (int i = 0; i < 3; i++)
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
            Quaternion tilted = rest * Quaternion.Euler(5f, 0f, 22f);
            yield return RotateBoneOverTime(head, rest, tilted, 0.3f);
            yield return new WaitForSeconds(0.8f);
            yield return RotateBoneOverTime(head, tilted, rest, 0.3f);
        }
        else
        {
            float angle = 18f;
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
        float amount = 0.04f;
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
        Vector3 squash = new Vector3(originalScale.x * 1.12f, originalScale.y * 0.78f, originalScale.z * 1.12f);

        yield return ScaleOverTime(squash, 0.1f);
        yield return ScaleOverTime(originalScale, 0.15f);

        ResetState();
    }

    /// <summary> Shoulders raise + arms out (shrug) — bone-based </summary>
    IEnumerator DoShrug()
    {
        isPlaying = true;
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (leftArm != null && rightArm != null)
        {
            Quaternion lRest = leftArm.localRotation;
            Quaternion rRest = rightArm.localRotation;
            Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
            Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
            Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;

            // Arms out + bent elbows (classic shrug pose)
            Quaternion lOut = lRest * Quaternion.Euler(-30f, 0f, -15f);
            Quaternion rOut = rRest * Quaternion.Euler(-30f, 0f, 15f);
            Quaternion llBent = llRest * Quaternion.Euler(-50f, 0f, 0f);
            Quaternion rlBent = rlRest * Quaternion.Euler(-50f, 0f, 0f);
            Quaternion hTilt = hRest * Quaternion.Euler(0f, 0f, 10f);

            // Raise to shrug
            yield return RotateBoneOverTime(leftArm, lRest, lOut, 0.2f);
            rightArm.localRotation = rOut;
            if (leftLower != null) leftLower.localRotation = llBent;
            if (rightLower != null) rightLower.localRotation = rlBent;
            if (head != null) yield return RotateBoneOverTime(head, hRest, hTilt, 0.15f);

            // Small body lift
            yield return MoveOverTime(Vector3.up, 0.04f, 0.1f);
            yield return new WaitForSeconds(0.5f);

            // Lower back
            yield return MoveOverTime(Vector3.up, -0.04f, 0.15f);
            if (head != null) yield return RotateBoneOverTime(head, hTilt, hRest, 0.15f);
            yield return RotateBoneOverTime(leftArm, lOut, lRest, 0.2f);
            rightArm.localRotation = rRest;
            if (leftLower != null) leftLower.localRotation = llRest;
            if (rightLower != null) rightLower.localRotation = rlRest;
        }
        else
        {
            yield return MoveOverTime(Vector3.up, 0.07f, 0.15f);
            yield return new WaitForSeconds(0.4f);
            yield return MoveOverTime(Vector3.up, -0.07f, 0.2f);
        }

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
                Quaternion left = rest * Quaternion.Euler(0f, -22f, 0f);
                Quaternion right = rest * Quaternion.Euler(0f, 22f, 0f);
                yield return RotateBoneOverTime(head, head.localRotation, left, 0.1f);
                yield return RotateBoneOverTime(head, left, right, 0.2f);
                yield return RotateBoneOverTime(head, right, rest, 0.1f);
            }
        }
        else
        {
            float angle = 16f;
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
            float hop = Mathf.Sin(t * Mathf.PI) * 0.14f;
            transform.localPosition = startPos + Vector3.up * hop;
            yield return null;
        }

        ResetState();
    }

    /// <summary> Side-to-side dance sway with bounce </summary>
    IEnumerator DoDance()
    {
        isPlaying = true;
        float swayAngle = 16f;
        float bounceHeight = 0.08f;
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
        float hideAmount = -0.18f;

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

    /// <summary> Pout with head down + arms crossed-ish — bone-based </summary>
    IEnumerator DoPout()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);

        if (head != null)
        {
            Quaternion hRest = head.localRotation;
            Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
            Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
            Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;

            // Head down, slight puff, arms tight
            Quaternion hDown = hRest * Quaternion.Euler(15f, 0f, 0f);
            Quaternion sSlouch = sRest * Quaternion.Euler(5f, 0f, 0f);
            Quaternion lTight = lRest * Quaternion.Euler(-20f, 15f, 10f);
            Quaternion rTight = rRest * Quaternion.Euler(-20f, -15f, -10f);

            yield return RotateBoneOverTime(head, hRest, hDown, 0.2f);
            if (spine != null) spine.localRotation = sSlouch;
            if (leftArm != null) leftArm.localRotation = lTight;
            if (rightArm != null) rightArm.localRotation = rTight;

            // Puff body + head shakes
            Vector3 puffScale = new Vector3(originalScale.x * 1.10f, originalScale.y * 0.93f, originalScale.z * 1.10f);
            yield return ScaleOverTime(puffScale, 0.15f);

            for (int i = 0; i < 3; i++)
            {
                Quaternion hLeft = hDown * Quaternion.Euler(0f, 0f, 8f);
                Quaternion hRight = hDown * Quaternion.Euler(0f, 0f, -8f);
                yield return RotateBoneOverTime(head, head.localRotation, hLeft, 0.1f);
                yield return RotateBoneOverTime(head, hLeft, hRight, 0.2f);
                yield return RotateBoneOverTime(head, hRight, hDown, 0.1f);
            }

            yield return new WaitForSeconds(0.2f);

            // Return
            yield return ScaleOverTime(originalScale, 0.2f);
            yield return RotateBoneOverTime(head, hDown, hRest, 0.2f);
            if (spine != null) spine.localRotation = sRest;
            if (leftArm != null) leftArm.localRotation = lRest;
            if (rightArm != null) rightArm.localRotation = rRest;
        }
        else
        {
            Vector3 puffScale = new Vector3(originalScale.x * 1.14f, originalScale.y * 0.90f, originalScale.z * 1.14f);
            yield return ScaleOverTime(puffScale, 0.2f);
            for (int i = 0; i < 3; i++)
            {
                yield return RotateOverTime(Vector3.forward, 8f, 0.1f);
                yield return RotateOverTime(Vector3.forward, -16f, 0.2f);
                yield return RotateOverTime(Vector3.forward, 8f, 0.1f);
            }
            yield return new WaitForSeconds(0.2f);
            yield return ScaleOverTime(originalScale, 0.3f);
        }

        ResetState();
    }

    /// <summary> Clap with arm bones meeting in front </summary>
    IEnumerator DoClap()
    {
        isPlaying = true;
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        if (leftArm != null && rightArm != null)
        {
            Quaternion lRest = leftArm.localRotation;
            Quaternion rRest = rightArm.localRotation;
            Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
            Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;

            // Arms forward + bent (clap position)
            Quaternion lForward = lRest * Quaternion.Euler(-50f, 20f, 15f);
            Quaternion rForward = rRest * Quaternion.Euler(-50f, -20f, -15f);
            Quaternion llBent = llRest * Quaternion.Euler(-60f, 0f, 0f);
            Quaternion rlBent = rlRest * Quaternion.Euler(-60f, 0f, 0f);
            // Arms slightly apart (open)
            Quaternion lOpen = lRest * Quaternion.Euler(-50f, 10f, 25f);
            Quaternion rOpen = rRest * Quaternion.Euler(-50f, -10f, -25f);

            // Raise to clap position
            yield return RotateBoneOverTime(leftArm, lRest, lForward, 0.15f);
            rightArm.localRotation = rForward;
            if (leftLower != null) leftLower.localRotation = llBent;
            if (rightLower != null) rightLower.localRotation = rlBent;

            // Clap cycles
            for (int i = 0; i < 5; i++)
            {
                // Open
                leftArm.localRotation = lOpen;
                rightArm.localRotation = rOpen;
                yield return new WaitForSeconds(0.06f);
                // Close (clap!)
                leftArm.localRotation = lForward;
                rightArm.localRotation = rForward;
                yield return new WaitForSeconds(0.06f);
                // Tiny bounce with each clap
                yield return MoveOverTime(Vector3.up, 0.04f, 0.03f);
                yield return MoveOverTime(Vector3.up, -0.04f, 0.03f);
            }

            // Lower arms back
            yield return RotateBoneOverTime(leftArm, leftArm.localRotation, lRest, 0.2f);
            rightArm.localRotation = rRest;
            if (leftLower != null) leftLower.localRotation = llRest;
            if (rightLower != null) rightLower.localRotation = rlRest;
        }
        else
        {
            // Fallback: scale-based clap
            float squishAmount = 0.07f;
            float speed = 0.06f;
            for (int i = 0; i < 5; i++)
            {
                Vector3 squished = new Vector3(originalScale.x * 0.88f, originalScale.y * 1.06f, originalScale.z);
                yield return ScaleOverTime(squished, speed);
                yield return ScaleOverTime(originalScale, speed);
                yield return MoveOverTime(Vector3.up, squishAmount, speed * 0.5f);
                yield return MoveOverTime(Vector3.up, -squishAmount, speed * 0.5f);
            }
        }

        ResetState();
    }

    /// <summary> Jolt back in surprise — bone-based (gasp) </summary>
    IEnumerator DoGasp()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);

        if (head != null)
        {
            Quaternion hRest = head.localRotation;
            Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
            Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
            Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;

            // Head jolts back, spine arches, arms flinch outward
            Quaternion hBack = hRest * Quaternion.Euler(-20f, 0f, 0f);
            Quaternion sBack = sRest * Quaternion.Euler(-10f, 0f, 0f);
            Quaternion lFlinch = lRest * Quaternion.Euler(-25f, 0f, -20f);
            Quaternion rFlinch = rRest * Quaternion.Euler(-25f, 0f, 20f);

            // Quick snap back
            head.localRotation = hBack;
            if (spine != null) spine.localRotation = sBack;
            if (leftArm != null) leftArm.localRotation = lFlinch;
            if (rightArm != null) rightArm.localRotation = rFlinch;

            // Body jolts up
            yield return MoveOverTime(Vector3.up, 0.08f, 0.06f);
            yield return new WaitForSeconds(0.5f);

            // Settle back smoothly
            yield return MoveOverTime(Vector3.up, -0.08f, 0.25f);
            yield return RotateBoneOverTime(head, hBack, hRest, 0.3f);
            if (spine != null) yield return RotateBoneOverTime(spine, sBack, sRest, 0.2f);
            if (leftArm != null) yield return RotateBoneOverTime(leftArm, lFlinch, lRest, 0.2f);
            if (rightArm != null) rightArm.localRotation = rRest;
        }
        else
        {
            Vector3 startPos = transform.localPosition;
            Vector3 joltPos = startPos + new Vector3(0f, 0.10f, -0.04f);
            Vector3 tallScale = new Vector3(originalScale.x * 0.85f, originalScale.y * 1.18f, originalScale.z * 0.85f);
            yield return ScaleOverTime(tallScale, 0.08f);
            transform.localPosition = joltPos;
            yield return new WaitForSeconds(0.5f);
            yield return ScaleOverTime(originalScale, 0.3f);
            float elapsed = 0f;
            while (elapsed < 0.3f)
            {
                elapsed += Time.deltaTime;
                transform.localPosition = Vector3.Lerp(joltPos, originalPos, Mathf.SmoothStep(0, 1, elapsed / 0.3f));
                yield return null;
            }
        }

        ResetState();
    }

    /// <summary> Gentle forward lean with arms reaching — bone-based (comfort/pat/hug) </summary>
    IEnumerator DoGentle()
    {
        isPlaying = true;
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);

        if (spine != null)
        {
            Quaternion sRest = spine.localRotation;
            Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
            Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
            Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;

            // Lean forward + arms reach out gently
            Quaternion sLean = sRest * Quaternion.Euler(14f, 0f, 0f);
            Quaternion hDown = hRest * Quaternion.Euler(8f, 0f, 0f);
            Quaternion lReach = lRest * Quaternion.Euler(-35f, 0f, 10f);
            Quaternion rReach = rRest * Quaternion.Euler(-35f, 0f, -10f);

            yield return RotateBoneOverTime(spine, sRest, sLean, 0.3f);
            if (head != null) head.localRotation = hDown;
            if (leftArm != null) yield return RotateBoneOverTime(leftArm, lRest, lReach, 0.25f);
            if (rightArm != null) rightArm.localRotation = rReach;

            yield return new WaitForSeconds(0.8f);

            // Return
            if (leftArm != null) yield return RotateBoneOverTime(leftArm, lReach, lRest, 0.25f);
            if (rightArm != null) rightArm.localRotation = rRest;
            if (head != null) head.localRotation = hRest;
            yield return RotateBoneOverTime(spine, sLean, sRest, 0.3f);
        }
        else
        {
            yield return RotateOverTime(Vector3.right, 14f, 0.3f);
            yield return new WaitForSeconds(0.8f);
            yield return RotateOverTime(Vector3.right, -14f, 0.3f);
        }

        ResetState();
    }

    // --- New Bone-Based Emote Animations ---

    /// <summary> Point forward with right arm </summary>
    IEnumerator DoPoint()
    {
        isPlaying = true;
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (rightArm != null)
        {
            Quaternion aRest = rightArm.localRotation;
            Quaternion lRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;

            // Raise arm forward + straighten
            Quaternion aPoint = aRest * Quaternion.Euler(-70f, 0f, -25f);
            Quaternion lStraight = lRest * Quaternion.Euler(-10f, 0f, 0f);
            // Head tilts slightly in the direction
            Quaternion hLook = hRest * Quaternion.Euler(-5f, 10f, 0f);

            yield return RotateBoneOverTime(rightArm, aRest, aPoint, 0.2f);
            if (rightLower != null) rightLower.localRotation = lStraight;
            if (head != null) yield return RotateBoneOverTime(head, hRest, hLook, 0.15f);

            yield return new WaitForSeconds(0.6f);

            // Return
            if (head != null) yield return RotateBoneOverTime(head, hLook, hRest, 0.15f);
            if (rightLower != null) rightLower.localRotation = lRest;
            yield return RotateBoneOverTime(rightArm, aPoint, aRest, 0.25f);
        }
        else
        {
            yield return RotateOverTime(Vector3.forward, -10f, 0.2f);
            yield return new WaitForSeconds(0.6f);
            yield return RotateOverTime(Vector3.forward, 10f, 0.2f);
        }

        ResetState();
    }

    /// <summary> Facepalm — hand to face </summary>
    IEnumerator DoFacepalm()
    {
        isPlaying = true;
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        if (rightArm != null)
        {
            Quaternion aRest = rightArm.localRotation;
            Quaternion lRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
            Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;

            // Raise hand to face
            Quaternion aUp = aRest * Quaternion.Euler(-55f, 0f, -15f);
            Quaternion lBent = lRest * Quaternion.Euler(-90f, 0f, 0f);
            Quaternion hDown = hRest * Quaternion.Euler(12f, 0f, 0f);
            Quaternion sSlouch = sRest * Quaternion.Euler(5f, 0f, 0f);

            yield return RotateBoneOverTime(rightArm, aRest, aUp, 0.25f);
            if (rightLower != null) yield return RotateBoneOverTime(rightLower, lRest, lBent, 0.2f);
            if (head != null) yield return RotateBoneOverTime(head, hRest, hDown, 0.2f);
            if (spine != null) spine.localRotation = sSlouch;

            yield return new WaitForSeconds(0.8f);

            // Recover
            if (spine != null) spine.localRotation = sRest;
            if (head != null) yield return RotateBoneOverTime(head, hDown, hRest, 0.2f);
            if (rightLower != null) yield return RotateBoneOverTime(rightLower, lBent, lRest, 0.2f);
            yield return RotateBoneOverTime(rightArm, aUp, aRest, 0.25f);
        }
        else
        {
            yield return RotateOverTime(Vector3.right, 10f, 0.25f);
            yield return new WaitForSeconds(0.8f);
            yield return RotateOverTime(Vector3.right, -10f, 0.25f);
        }

        ResetState();
    }

    /// <summary> Look away sharply — head turns to side </summary>
    IEnumerator DoLookAway()
    {
        isPlaying = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        if (head != null)
        {
            Quaternion hRest = head.localRotation;
            Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;

            // Turn head and body away
            Quaternion hAway = hRest * Quaternion.Euler(5f, -30f, -5f);
            Quaternion sAway = sRest * Quaternion.Euler(0f, -8f, 0f);

            yield return RotateBoneOverTime(head, hRest, hAway, 0.2f);
            if (spine != null) yield return RotateBoneOverTime(spine, sRest, sAway, 0.15f);

            yield return new WaitForSeconds(0.7f);

            // Glance back
            if (spine != null) yield return RotateBoneOverTime(spine, sAway, sRest, 0.2f);
            yield return RotateBoneOverTime(head, hAway, hRest, 0.25f);
        }
        else
        {
            yield return RotateOverTime(Vector3.up, -20f, 0.2f);
            yield return new WaitForSeconds(0.7f);
            yield return RotateOverTime(Vector3.up, 20f, 0.25f);
        }

        ResetState();
    }

    /// <summary> Rest chin on hand — thinking pose </summary>
    IEnumerator DoChinRest()
    {
        isPlaying = true;
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (rightArm != null)
        {
            Quaternion aRest = rightArm.localRotation;
            Quaternion lRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;

            // Hand to chin, elbow bent sharply
            Quaternion aUp = aRest * Quaternion.Euler(-40f, 0f, -10f);
            Quaternion lBent = lRest * Quaternion.Euler(-100f, 0f, 0f);
            Quaternion hTilt = hRest * Quaternion.Euler(5f, 8f, 8f);

            yield return RotateBoneOverTime(rightArm, aRest, aUp, 0.25f);
            if (rightLower != null) yield return RotateBoneOverTime(rightLower, lRest, lBent, 0.2f);
            if (head != null) yield return RotateBoneOverTime(head, hRest, hTilt, 0.2f);

            yield return new WaitForSeconds(1.0f);

            // Return
            if (head != null) yield return RotateBoneOverTime(head, hTilt, hRest, 0.2f);
            if (rightLower != null) yield return RotateBoneOverTime(rightLower, lBent, lRest, 0.2f);
            yield return RotateBoneOverTime(rightArm, aUp, aRest, 0.25f);
        }
        else
        {
            yield return RotateOverTime(Vector3.forward, 12f, 0.25f);
            yield return new WaitForSeconds(1.0f);
            yield return RotateOverTime(Vector3.forward, -12f, 0.25f);
        }

        ResetState();
    }

    /// <summary> Cross arms — assertive/waiting pose </summary>
    IEnumerator DoCrossArms()
    {
        isPlaying = true;
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (leftArm != null && rightArm != null)
        {
            Quaternion lRest = leftArm.localRotation;
            Quaternion rRest = rightArm.localRotation;
            Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
            Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;

            // Arms across chest
            Quaternion lCross = lRest * Quaternion.Euler(-30f, 25f, 15f);
            Quaternion rCross = rRest * Quaternion.Euler(-30f, -25f, -15f);
            Quaternion llBent = llRest * Quaternion.Euler(-80f, 0f, 0f);
            Quaternion rlBent = rlRest * Quaternion.Euler(-80f, 0f, 0f);
            Quaternion hTilt = hRest * Quaternion.Euler(0f, 0f, -6f);

            yield return RotateBoneOverTime(leftArm, lRest, lCross, 0.25f);
            rightArm.localRotation = rCross;
            if (leftLower != null) leftLower.localRotation = llBent;
            if (rightLower != null) rightLower.localRotation = rlBent;
            if (head != null) yield return RotateBoneOverTime(head, hRest, hTilt, 0.15f);

            yield return new WaitForSeconds(0.8f);

            // Uncross
            if (head != null) yield return RotateBoneOverTime(head, hTilt, hRest, 0.15f);
            yield return RotateBoneOverTime(leftArm, lCross, lRest, 0.25f);
            rightArm.localRotation = rRest;
            if (leftLower != null) leftLower.localRotation = llRest;
            if (rightLower != null) rightLower.localRotation = rlRest;
        }
        else
        {
            yield return RotateOverTime(Vector3.right, 5f, 0.2f);
            yield return new WaitForSeconds(0.8f);
            yield return RotateOverTime(Vector3.right, -5f, 0.2f);
        }

        ResetState();
    }

    /// <summary> Both arms raised up — celebration/hands up </summary>
    IEnumerator DoHandsUp()
    {
        isPlaying = true;
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        if (leftArm != null && rightArm != null)
        {
            Quaternion lRest = leftArm.localRotation;
            Quaternion rRest = rightArm.localRotation;
            Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
            Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;

            // Both arms straight up
            Quaternion lUp = lRest * Quaternion.Euler(-140f, 0f, 15f);
            Quaternion rUp = rRest * Quaternion.Euler(-140f, 0f, -15f);
            Quaternion llStr = llRest * Quaternion.Euler(-10f, 0f, 0f);
            Quaternion rlStr = rlRest * Quaternion.Euler(-10f, 0f, 0f);
            Quaternion sArch = sRest * Quaternion.Euler(-5f, 0f, 0f);

            yield return RotateBoneOverTime(leftArm, lRest, lUp, 0.25f);
            rightArm.localRotation = rUp;
            if (leftLower != null) leftLower.localRotation = llStr;
            if (rightLower != null) rightLower.localRotation = rlStr;
            if (spine != null) spine.localRotation = sArch;

            // Victory bounces
            for (int i = 0; i < 3; i++)
            {
                yield return MoveOverTime(Vector3.up, 0.08f, 0.08f);
                yield return MoveOverTime(Vector3.up, -0.08f, 0.08f);
            }

            // Lower arms
            if (spine != null) spine.localRotation = sRest;
            yield return RotateBoneOverTime(leftArm, lUp, lRest, 0.3f);
            rightArm.localRotation = rRest;
            if (leftLower != null) leftLower.localRotation = llRest;
            if (rightLower != null) rightLower.localRotation = rlRest;
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                yield return MoveOverTime(Vector3.up, 0.12f, 0.1f);
                yield return MoveOverTime(Vector3.up, -0.12f, 0.1f);
            }
        }

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
