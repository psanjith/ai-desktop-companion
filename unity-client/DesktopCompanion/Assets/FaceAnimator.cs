using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRM;

/// <summary>
/// Drives VRM facial expressions (blend shapes) for emotions and auto-blink.
/// Attached automatically by CharacterManager when a VRM model loads.
/// </summary>
public class FaceAnimator : MonoBehaviour
{
    [Header("Blink Settings")]
    public float blinkInterval = 3.5f;
    public float blinkVariation = 2f;
    public float blinkDuration = 0.12f;

    [Header("Expression Settings")]
    public float expressionFadeSpeed = 3f;
    public float expressionHoldTime = 4f;

    // VRM blend shape proxy — the key to facial animation
    private VRMBlendShapeProxy blendProxy;

    // Current emotion state
    private BlendShapePreset currentEmotion = BlendShapePreset.Neutral;
    private float currentEmotionValue = 0f;
    private float targetEmotionValue = 0f;
    private BlendShapePreset targetEmotion = BlendShapePreset.Neutral;
    private BlendShapePreset fadingOutEmotion = BlendShapePreset.Neutral;
    private float fadingOutValue = 0f;

    // Blink state
    private bool isBlinking = false;

    // Lip sync placeholder
    private float currentMouthOpen = 0f;

    void Start()
    {
        blendProxy = GetComponentInChildren<VRMBlendShapeProxy>();
        if (blendProxy == null)
        {
            Debug.LogWarning("FaceAnimator: No VRMBlendShapeProxy found — facial animations disabled.");
            enabled = false;
            return;
        }

        StartCoroutine(AutoBlink());
    }

    void Update()
    {
        if (blendProxy == null) return;

        // Fade out the previous emotion if switching
        if (fadingOutValue > 0f && fadingOutEmotion != currentEmotion)
        {
            fadingOutValue = Mathf.MoveTowards(fadingOutValue, 0f, Time.deltaTime * expressionFadeSpeed * 2f);
            blendProxy.AccumulateValue(
                BlendShapeKey.CreateFromPreset(fadingOutEmotion), fadingOutValue);
        }

        // Smoothly blend toward target emotion value
        currentEmotionValue = Mathf.MoveTowards(currentEmotionValue, targetEmotionValue,
            Time.deltaTime * expressionFadeSpeed);

        if (currentEmotion != BlendShapePreset.Neutral)
        {
            blendProxy.AccumulateValue(
                BlendShapeKey.CreateFromPreset(currentEmotion), currentEmotionValue);
        }

        // Apply mouth open (for future lip sync or talking indicator)
        blendProxy.AccumulateValue(
            BlendShapeKey.CreateFromPreset(BlendShapePreset.A), currentMouthOpen);

        blendProxy.Apply();
    }

    // ===================== Public API =====================

    /// <summary>
    /// Set the character's facial expression by emotion name.
    /// Called by CompanionController when the AI responds.
    /// </summary>
    public void SetEmotion(string emotion)
    {
        BlendShapePreset preset = EmotionToPreset(emotion);

        // If switching emotions, fade out the old one
        if (currentEmotion != preset && currentEmotionValue > 0.05f)
        {
            fadingOutEmotion = currentEmotion;
            fadingOutValue = currentEmotionValue;
        }

        currentEmotion = preset;
        targetEmotion = preset;

        if (preset == BlendShapePreset.Neutral)
        {
            targetEmotionValue = 0f;
        }
        else
        {
            targetEmotionValue = 1f;
            // Auto-fade after hold time
            StopCoroutine("AutoFadeEmotion");
            StartCoroutine(AutoFadeEmotion(expressionHoldTime));
        }
    }

    /// <summary>
    /// Map an emote keyword to a facial expression.
    /// Called with the same emote string as EmoteAnimator.
    /// </summary>
    public void SetEmotionFromEmote(string emote)
    {
        string lower = emote.ToLower().Trim();

        if (lower.Contains("giggle") || lower.Contains("laugh") || lower.Contains("happy")
            || lower.Contains("excited") || lower.Contains("chuckle"))
            SetEmotion("joy");
        else if (lower.Contains("angry") || lower.Contains("grr") || lower.Contains("frown")
            || lower.Contains("frustrated"))
            SetEmotion("angry");
        else if (lower.Contains("sad") || lower.Contains("sigh") || lower.Contains("tear")
            || lower.Contains("cry") || lower.Contains("sorrow"))
            SetEmotion("sorrow");
        else if (lower.Contains("blush") || lower.Contains("shy") || lower.Contains("embarrass")
            || lower.Contains("wiggle") || lower.Contains("fun"))
            SetEmotion("fun");
        else if (lower.Contains("yawn") || lower.Contains("tired") || lower.Contains("sleepy"))
            SetEmotion("sorrow"); // Relaxed/droopy look
        else if (lower.Contains("think") || lower.Contains("ponder") || lower.Contains("hmm"))
            SetEmotion("neutral"); // Thoughtful = calm face
        else if (lower.Contains("wave") || lower.Contains("nod") || lower.Contains("bounce")
            || lower.Contains("jump"))
            SetEmotion("joy"); // Positive actions = happy face
        else if (lower.Contains("shake") || lower.Contains("no"))
            SetEmotion("angry"); // Disagreement
        else
            SetEmotion("fun"); // Default to a pleasant expression
    }

    /// <summary>
    /// Open the mouth (0-1) for talking animation.
    /// </summary>
    public void SetMouthOpen(float amount)
    {
        currentMouthOpen = Mathf.Clamp01(amount);
    }

    /// <summary>
    /// Quickly pulse the mouth open/closed for a "talking" effect.
    /// </summary>
    public void StartTalking()
    {
        StopCoroutine("TalkingAnimation");
        StartCoroutine(TalkingAnimation());
    }

    public void StopTalking()
    {
        StopCoroutine("TalkingAnimation");
        currentMouthOpen = 0f;
    }

    /// <summary>
    /// Reset face to neutral immediately.
    /// </summary>
    public void ResetFace()
    {
        StopAllCoroutines();
        currentEmotion = BlendShapePreset.Neutral;
        targetEmotionValue = 0f;
        currentEmotionValue = 0f;
        fadingOutValue = 0f;
        currentMouthOpen = 0f;

        if (blendProxy != null)
        {
            blendProxy.ImmediatelySetValue(
                BlendShapeKey.CreateFromPreset(BlendShapePreset.Joy), 0f);
            blendProxy.ImmediatelySetValue(
                BlendShapeKey.CreateFromPreset(BlendShapePreset.Angry), 0f);
            blendProxy.ImmediatelySetValue(
                BlendShapeKey.CreateFromPreset(BlendShapePreset.Sorrow), 0f);
            blendProxy.ImmediatelySetValue(
                BlendShapeKey.CreateFromPreset(BlendShapePreset.Fun), 0f);
            blendProxy.ImmediatelySetValue(
                BlendShapeKey.CreateFromPreset(BlendShapePreset.A), 0f);
            blendProxy.ImmediatelySetValue(
                BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink), 0f);
        }

        StartCoroutine(AutoBlink());
    }

    // ===================== Internal =====================

    BlendShapePreset EmotionToPreset(string emotion)
    {
        switch (emotion.ToLower().Trim())
        {
            case "joy":
            case "happy":
                return BlendShapePreset.Joy;
            case "angry":
            case "anger":
                return BlendShapePreset.Angry;
            case "sorrow":
            case "sad":
                return BlendShapePreset.Sorrow;
            case "fun":
            case "playful":
                return BlendShapePreset.Fun;
            default:
                return BlendShapePreset.Neutral;
        }
    }

    IEnumerator AutoFadeEmotion(float holdTime)
    {
        yield return new WaitForSeconds(holdTime);
        targetEmotionValue = 0f;
        // Once faded, reset to neutral
        yield return new WaitForSeconds(1f / expressionFadeSpeed + 0.1f);
        if (targetEmotionValue == 0f)
            currentEmotion = BlendShapePreset.Neutral;
    }

    IEnumerator AutoBlink()
    {
        while (true)
        {
            float wait = blinkInterval + Random.Range(-blinkVariation, blinkVariation);
            yield return new WaitForSeconds(Mathf.Max(0.5f, wait));

            if (blendProxy != null && !isBlinking)
            {
                isBlinking = true;

                // Close eyes
                float t = 0f;
                while (t < blinkDuration / 2f)
                {
                    t += Time.deltaTime;
                    float val = Mathf.Lerp(0f, 1f, t / (blinkDuration / 2f));
                    blendProxy.ImmediatelySetValue(
                        BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink), val);
                    yield return null;
                }

                // Open eyes
                t = 0f;
                while (t < blinkDuration / 2f)
                {
                    t += Time.deltaTime;
                    float val = Mathf.Lerp(1f, 0f, t / (blinkDuration / 2f));
                    blendProxy.ImmediatelySetValue(
                        BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink), val);
                    yield return null;
                }

                blendProxy.ImmediatelySetValue(
                    BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink), 0f);
                isBlinking = false;
            }
        }
    }

    IEnumerator TalkingAnimation()
    {
        while (true)
        {
            // Random mouth shapes to simulate talking
            float target = Random.Range(0.2f, 0.8f);
            float duration = Random.Range(0.05f, 0.15f);
            float elapsed = 0f;
            float start = currentMouthOpen;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                currentMouthOpen = Mathf.Lerp(start, target, elapsed / duration);
                yield return null;
            }

            // Brief hold
            yield return new WaitForSeconds(Random.Range(0.02f, 0.08f));
        }
    }
}
