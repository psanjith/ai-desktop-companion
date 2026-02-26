using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays procedural emote animations on the character.
/// Emote keywords from the LLM are mapped to transform-based animations.
/// </summary>
public class EmoteAnimator : MonoBehaviour
{
    private bool isPlaying = false;
    private Vector3 originalPos;
    private Quaternion originalRot;
    private Vector3 originalScale;

    void Start()
    {
        CaptureOriginalState();
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
        else if (lower.Contains("wave"))
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
        else if (lower.Contains("shake") || lower.Contains("no"))
            StartCoroutine(DoHeadShake());
        else
            StartCoroutine(DoBounce()); // Default: gentle bounce
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

    /// <summary> Quick nods (agreement) </summary>
    IEnumerator DoNod()
    {
        isPlaying = true;
        float nodAngle = 10f;
        float speed = 0.15f;

        for (int i = 0; i < 3; i++)
        {
            yield return RotateOverTime(Vector3.right, nodAngle, speed);
            yield return RotateOverTime(Vector3.right, -nodAngle, speed);
        }

        ResetState();
    }

    /// <summary> Side-to-side sway (wave) </summary>
    IEnumerator DoWave()
    {
        isPlaying = true;
        float angle = 12f;
        float speed = 0.2f;

        for (int i = 0; i < 3; i++)
        {
            yield return RotateOverTime(Vector3.forward, angle, speed);
            yield return RotateOverTime(Vector3.forward, -angle * 2f, speed * 2f);
            yield return RotateOverTime(Vector3.forward, angle, speed);
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

    /// <summary> Tilt to side (thinking) </summary>
    IEnumerator DoTiltSide()
    {
        isPlaying = true;
        float angle = 12f;

        yield return RotateOverTime(Vector3.forward, angle, 0.3f);
        yield return new WaitForSeconds(0.8f);
        yield return RotateOverTime(Vector3.forward, -angle, 0.3f);

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

    /// <summary> Head shake side to side (no/disagree) </summary>
    IEnumerator DoHeadShake()
    {
        isPlaying = true;
        float angle = 10f;
        float speed = 0.1f;

        for (int i = 0; i < 3; i++)
        {
            yield return RotateOverTime(Vector3.up, angle, speed);
            yield return RotateOverTime(Vector3.up, -angle * 2f, speed * 2f);
            yield return RotateOverTime(Vector3.up, angle, speed);
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

    private void ResetState()
    {
        transform.localPosition = originalPos;
        transform.localRotation = originalRot;
        transform.localScale = originalScale;
        isPlaying = false;
    }
}
