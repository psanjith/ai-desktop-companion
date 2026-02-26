using UnityEngine;

/// <summary>
/// Simple idle animation — gentle bobbing and breathing effect.
/// Attached automatically by CharacterManager when a model loads.
/// </summary>
public class IdleAnimator : MonoBehaviour
{
    [Header("Bobbing (up/down sway)")]
    public float bobAmount = 0.02f;
    public float bobSpeed = 1.5f;

    [Header("Breathing (scale pulse)")]
    public float breatheAmount = 0.005f;
    public float breatheSpeed = 3f;

    private Vector3 startPos;
    private float baseScale = 1f;

    void Start()
    {
        startPos = transform.localPosition;
        baseScale = transform.localScale.x;
    }

    void Update()
    {
        // Gentle bobbing
        float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
        transform.localPosition = startPos + new Vector3(0f, bobOffset, 0f);

        // Breathing — subtle scale pulse
        float breatheOffset = Mathf.Sin(Time.time * breatheSpeed) * breatheAmount;
        float s = baseScale + breatheOffset;
        transform.localScale = new Vector3(s, s, s);
    }

    /// <summary>
    /// Call this when the character is resized externally.
    /// </summary>
    public void SetBaseScale(float scale)
    {
        baseScale = scale;
    }
}
