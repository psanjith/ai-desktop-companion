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
    private bool isGesturing = false;
    private Vector3 originalPos;
    private Quaternion originalRot;
    private Vector3 originalScale;
    private PoseManager poseManager;
    private IdleAnimator idleAnimator;
    private Coroutine gestureLoopRef;

    /// <summary>
    /// Animation personality. Set by CharacterManager when the character loads.
    /// Expressive = Luna (big, varied, physically reactive).
    /// Reserved   = Ren  (subtle, deliberate, understated).
    /// </summary>
    public enum CharacterStyle { Expressive, Reserved }
    public CharacterStyle style = CharacterStyle.Expressive;

    // --- Context-aware gesture tracking ---
    private string currentStreamText = "";    // accumulated text from streaming
    private int lastGestureTextPos = 0;       // position in text when last gesture fired
    private int lastGestureCategory = -1;     // avoid repeating same category twice

    /// <summary> True if a full emote animation is currently playing. </summary>
    public bool IsPlaying => isPlaying;

    /// <summary>
    /// Update the current streaming text so gestures can be contextual.
    /// Call from CompanionController as tokens arrive.
    /// </summary>
    public void UpdateStreamText(string text)
    {
        currentStreamText = text ?? "";
    }

    void Start()
    {
        CaptureOriginalState();
        poseManager = GetComponent<PoseManager>();
        idleAnimator = GetComponent<IdleAnimator>();
    }

    public void CaptureOriginalState()
    {
        originalPos = transform.localPosition;
        originalRot = transform.localRotation;
        originalScale = transform.localScale;
    }

    // ===================== Talking Gesture System =====================
    // Continuous small bone movements while the character is "speaking".
    // Runs on a loop, pauses for full emotes, resumes automatically.

    /// <summary>
    /// Start looping random talking gestures. Call when streaming begins.
    /// </summary>
    public void StartTalkingGestures()
    {
        StopTalkingGestures(); // prevent duplicates
        currentStreamText = "";
        lastGestureTextPos = 0;
        lastGestureCategory = -1;
        gestureLoopRef = StartCoroutine(TalkingGestureLoop());
        Debug.Log("EmoteAnimator: Talking gestures STARTED");
    }

    /// <summary>
    /// Stop the talking gesture loop. Call when streaming ends.
    /// </summary>
    public void StopTalkingGestures()
    {
        if (gestureLoopRef != null)
        {
            StopCoroutine(gestureLoopRef);
            gestureLoopRef = null;
        }
        if (isGesturing)
        {
            isGesturing = false;
            // Smooth blend back instead of snapping
            if (poseManager != null)
                StartCoroutine(poseManager.SmoothResetToRestPose(0.35f));
            if (idleAnimator != null) idleAnimator.paused = false;
        }
        Debug.Log("EmoteAnimator: Talking gestures STOPPED");
    }

    IEnumerator TalkingGestureLoop()
    {
        // Longer initial delay so the character settles before gesturing
        yield return new WaitForSeconds(0.8f);

        while (true)
        {
            // Wait if a full emote is currently playing
            while (isPlaying) yield return null;

            // Wait until we have enough new text to analyze
            // (at least ~25 chars since last gesture = ~5 words)
            while (currentStreamText.Length - lastGestureTextPos < 25)
            {
                if (isPlaying) break;
                yield return null;
            }
            if (isPlaying) continue;

            // Play a contextually relevant gesture based on recent text
            yield return PlayContextualGesture();

            // Relaxed pause between gestures (2.0 - 4.0s) for calm rhythm
            float wait = Random.Range(2.0f, 4.0f);
            float elapsed = 0f;
            while (elapsed < wait)
            {
                elapsed += Time.deltaTime;
                if (isPlaying) break;
                yield return null;
            }
        }
    }

    /// <summary>
    /// Analyze the recent stream text and pick a gesture that matches the content.
    /// Falls back to a contextually neutral conversational gesture.
    /// </summary>
    IEnumerator PlayContextualGesture()
    {
        if (isPlaying || isGesturing) yield break;

        isGesturing = true;
        if (idleAnimator != null) idleAnimator.paused = true;

        // Get the new text since our last gesture
        string recentText = "";
        if (currentStreamText.Length > lastGestureTextPos)
            recentText = currentStreamText.Substring(lastGestureTextPos).ToLower();
        else
            recentText = currentStreamText.ToLower();
        lastGestureTextPos = currentStreamText.Length;

        // Determine gesture category from text content
        // Categories: 0=question, 1=agreement, 2=explanation, 3=self-reference,
        //             4=uncertainty, 5=excitement, 6=negative, 7=thinking, 8=conversational
        int category = AnalyzeTextForGesture(recentText);

        // Avoid same category twice in a row — shift to a neighbor
        if (category == lastGestureCategory)
        {
            category = (category + Random.Range(1, 4)) % 9;
        }
        lastGestureCategory = category;

        Debug.Log($"EmoteAnimator: Context gesture cat={category} text='{recentText.Substring(0, Mathf.Min(40, recentText.Length))}'");

        // Reserved style (Ren): subtle head/body-only gestures — no big arm waves
        if (style == CharacterStyle.Reserved)
        {
            switch (category)
            {
                case 0: yield return GestureHeadTilt(); break;
                case 1: yield return GestureSmallNod(); break;
                case 2:
                    if (Random.value > 0.5f) yield return GestureHandOut();
                    else yield return GestureHeadTurn();
                    break;
                case 3: yield return GestureHandToChest(); break;
                case 4: yield return GestureShoulderLift(); break;
                case 5: yield return GestureSmallNod(); break; // plays it cool, no hand raise
                case 6: yield return GestureSmallHeadShake(); break;
                case 7:
                    if (Random.value > 0.5f) yield return GestureChin();
                    else yield return GestureHeadTilt();
                    break;
                default:
                    int rRes = Random.Range(0, 3);
                    if (rRes == 0) yield return GestureHeadTurn();
                    else if (rRes == 1) yield return GestureBodyShift();
                    else yield return GestureSmallNod();
                    break;
            }
            if (poseManager != null) yield return poseManager.SmoothResetToRestPose(0.35f);
            if (idleAnimator != null) idleAnimator.paused = false;
            isGesturing = false;
            yield break;
        }

        switch (category)
        {
            case 0: // Question — head tilt or head turn (curious look)
                if (Random.value > 0.5f)
                    yield return GestureHeadTilt();
                else
                    yield return GestureHeadTurn();
                break;
            case 1: // Agreement/positive — nod or lean forward
                if (Random.value > 0.5f)
                    yield return GestureSmallNod();
                else
                    yield return GestureLeanForward();
                break;
            case 2: // Explaining — hand out, both hands out, or finger point
            {
                int r2 = Random.Range(0, 3);
                if (r2 == 0) yield return GestureHandOut();
                else if (r2 == 1) yield return GestureBothHandsOut();
                else yield return GestureFingerPoint();
                break;
            }
            case 3: // Self-reference — hand to chest
                yield return GestureHandToChest();
                break;
            case 4: // Uncertainty — shoulder lift or body shift
                if (Random.value > 0.5f)
                    yield return GestureShoulderLift();
                else
                    yield return GestureBodyShift();
                break;
            case 5: // Excitement — hand raise or both hands out
                if (Random.value > 0.5f)
                    yield return GestureHandRaise();
                else
                    yield return GestureBothHandsOut();
                break;
            case 6: // Negative/dismissal — head shake (small) or shoulder lift
                if (Random.value > 0.5f)
                    yield return GestureSmallHeadShake();
                else
                    yield return GestureShoulderLift();
                break;
            case 7: // Thinking/pondering — head tilt, hand to chest, or chin touch
            {
                int r7 = Random.Range(0, 3);
                if (r7 == 0) yield return GestureHeadTilt();
                else if (r7 == 1) yield return GestureHandToChest();
                else yield return GestureChin();
                break;
            }
            default: // Conversational/neutral — nod, lean, body shift, or head bob
            {
                int rD = Random.Range(0, 4);
                if (rD == 0) yield return GestureSmallNod();
                else if (rD == 1) yield return GestureLeanForward();
                else if (rD == 2) yield return GestureBodyShift();
                else yield return GestureHeadBob();
                break;
            }
        }

        // Smooth cleanup after gesture — blend bones back instead of snapping
        if (poseManager != null)
            yield return poseManager.SmoothResetToRestPose(0.35f);
        if (idleAnimator != null) idleAnimator.paused = false;
        isGesturing = false;
    }

    /// <summary>
    /// Analyze text content to determine the most appropriate gesture category.
    /// Returns: 0=question, 1=agreement, 2=explanation, 3=self, 4=uncertainty,
    ///          5=excitement, 6=negative, 7=thinking, 8=conversational
    /// </summary>
    private int AnalyzeTextForGesture(string text)
    {
        if (string.IsNullOrEmpty(text)) return 8;

        // Score each category — highest wins
        int[] scores = new int[9];

        // 0: Question
        if (text.Contains("?")) scores[0] += 3;
        if (text.Contains("who") || text.Contains("what") || text.Contains("when")
            || text.Contains("where") || text.Contains("why") || text.Contains("how")
            || text.Contains("which") || text.Contains("would you") || text.Contains("do you")
            || text.Contains("can you") || text.Contains("right?") || text.Contains("know?"))
            scores[0] += 2;

        // 1: Agreement / positive
        if (text.Contains("yes") || text.Contains("yeah") || text.Contains("yep")
            || text.Contains("sure") || text.Contains("right") || text.Contains("agree")
            || text.Contains("exactly") || text.Contains("correct") || text.Contains("true")
            || text.Contains("good") || text.Contains("great") || text.Contains("nice")
            || text.Contains("of course") || text.Contains("definitely") || text.Contains("absolutely"))
            scores[1] += 2;

        // 2: Explanation
        if (text.Contains("because") || text.Contains("since") || text.Contains("actually")
            || text.Contains("basically") || text.Contains("well,") || text.Contains("so,")
            || text.Contains("let me") || text.Contains("think about") || text.Contains("for example")
            || text.Contains("the thing is") || text.Contains("here's") || text.Contains("like,")
            || text.Contains("means") || text.Contains("works") || text.Contains("called"))
            scores[2] += 2;

        // 3: Self-reference
        if (text.Contains(" i ") || text.Contains("i'm") || text.Contains("i'll")
            || text.Contains("my ") || text.Contains("me ") || text.Contains("myself")
            || text.Contains("i'd") || text.Contains("i've") || text.Contains("personally"))
            scores[3] += 2;

        // 4: Uncertainty
        if (text.Contains("maybe") || text.Contains("perhaps") || text.Contains("not sure")
            || text.Contains("idk") || text.Contains("hmm") || text.Contains("dunno")
            || text.Contains("probably") || text.Contains("might") || text.Contains("could be")
            || text.Contains("i guess") || text.Contains("possibly") || text.Contains("kind of"))
            scores[4] += 2;

        // 5: Excitement
        if (text.Contains("!") || text.Contains("wow") || text.Contains("oh!")
            || text.Contains("amazing") || text.Contains("cool") || text.Contains("awesome")
            || text.Contains("love") || text.Contains("yay") || text.Contains("fun")
            || text.Contains("omg") || text.Contains("incredible") || text.Contains("haha")
            || text.Contains("hehe") || text.Contains("excited"))
            scores[5] += 2;

        // 6: Negative / dismissal
        if (text.Contains("no") || text.Contains("not") || text.Contains("don't")
            || text.Contains("can't") || text.Contains("won't") || text.Contains("never")
            || text.Contains("nah") || text.Contains("nope") || text.Contains("but")
            || text.Contains("however") || text.Contains("unfortunately") || text.Contains("sadly"))
            scores[6] += 2;

        // 7: Thinking / pondering
        if (text.Contains("think") || text.Contains("wonder") || text.Contains("imagine")
            || text.Contains("consider") || text.Contains("suppose") || text.Contains("ponder")
            || text.Contains("curious") || text.Contains("interesting") || text.Contains("let me see")
            || text.Contains("what if"))
            scores[7] += 2;

        // Find highest scoring category
        int best = 8;
        int bestScore = 0;
        for (int i = 0; i < scores.Length - 1; i++)
        {
            if (scores[i] > bestScore)
            {
                bestScore = scores[i];
                best = i;
            }
        }

        return best;
    }

    // --- Talking Gesture Coroutines ---
    // Each gesture adds micro-variation to timing and amplitude so no two
    // repetitions ever look identical — essential for humanlike feel.

    /// <summary>
    /// For Reserved characters (Ren): remap high-energy emote keywords to calm equivalents.
    /// Returns true if handled so PlayEmote() can skip the standard chain.
    /// </summary>
    private bool TryPlayReservedEmote(string lower)
    {
        // Greetings / waves → single acknowledging nod
        if (lower.Contains("wave") || lower.Contains("greet") || lower.Contains("hello")
            || lower.Contains("bye") || lower.Contains("hi ") || lower == "hi")
        { StartCoroutine(DoNod()); return true; }

        // High-energy excitement → cool shrug ("yeah, sure")
        if (lower.Contains("jump") || lower.Contains("excited") || lower.Contains("yay")
            || lower.Contains("hooray") || lower.Contains("woohoo") || lower.Contains("happy")
            || lower.Contains("joyful") || lower.Contains("overjoyed") || lower.Contains("bounce"))
        { StartCoroutine(DoShrug()); return true; }

        // Dancing / spinning → look away (too cool for that)
        if (lower.Contains("dance") || lower.Contains("spin") || lower.Contains("twirl")
            || lower.Contains("wiggle") || lower.Contains("sway") || lower.Contains("groove"))
        { StartCoroutine(DoLookAway()); return true; }

        // Laughter → dry chin rest (amused but composed)
        if (lower.Contains("giggle") || lower.Contains("laugh") || lower.Contains("chuckle")
            || lower.Contains("haha") || lower.Contains("lol") || lower.Contains("hehe")
            || lower.Contains("snicker") || lower.Contains("amuse"))
        { StartCoroutine(DoChinRest()); return true; }

        // Celebrations / cheering → single approval nod
        if (lower.Contains("clap") || lower.Contains("applaud") || lower.Contains("cheer")
            || lower.Contains("celebrat") || lower.Contains("bravo") || lower.Contains("hands up")
            || lower.Contains("victory"))
        { StartCoroutine(DoNod()); return true; }

        // Blushing / shyness → plays it cool, looks away
        if (lower.Contains("blush") || lower.Contains("peek") || lower.Contains("shy")
            || lower.Contains("embarrass") || lower.Contains("fluster"))
        { StartCoroutine(DoLookAway()); return true; }

        // Both-arms big wave / flailing → shrug
        if (lower.Contains("big wave") || lower.Contains("wave both") || lower.Contains("flail"))
        { StartCoroutine(DoShrug()); return true; }

        return false; // Standard chain handles everything else (cross arms, shrug, look away, etc.)
    }

    /// <summary> Add ±12% random variation to a duration for natural imprecision. </summary>
    private float Vary(float value) { return value * Random.Range(0.88f, 1.12f); }

    /// <summary> Add ±15% random variation to an angle for subtle uniqueness. </summary>
    private float VaryAngle(float angle) { return angle * Random.Range(0.85f, 1.15f); }

    IEnumerator GestureHeadTilt()
    {
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        if (head == null) yield break;
        Quaternion rest = head.localRotation;
        float dir = Random.value > 0.5f ? 1f : -1f;
        Quaternion tilted = rest * Quaternion.Euler(0f, 0f, VaryAngle(18f) * dir);
        yield return RotateBoneOverTime(head, rest, tilted, Vary(0.45f), EaseInOutBack);
        yield return new WaitForSeconds(Vary(0.45f));
        yield return RotateBoneOverTime(head, tilted, rest, Vary(0.4f), SmootherStep);
    }

    IEnumerator GestureHandRaise()
    {
        bool useRight = Random.value > 0.5f;
        Transform arm = poseManager?.GetBone(useRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        Transform lower = poseManager?.GetBone(useRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        Transform hand = poseManager?.GetBone(useRight ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (arm == null) yield break;

        Quaternion aRest = arm.localRotation;
        Quaternion lRest = lower != null ? lower.localRotation : Quaternion.identity;
        Quaternion hRest = hand != null ? hand.localRotation : Quaternion.identity;

        float side = useRight ? -1f : 1f;
        Quaternion aUp = aRest * Quaternion.Euler(VaryAngle(-40f), 0f, VaryAngle(15f) * side);
        Quaternion lBent = lRest * Quaternion.Euler(VaryAngle(-45f), 0f, 0f);
        Quaternion hTilt = hRest * Quaternion.Euler(0f, 0f, VaryAngle(15f) * side);

        // Raise: arm leads, elbow cascades, hand follows
        float dur = Vary(0.45f);
        StartCoroutine(RotateBoneOverTime(arm, aRest, aUp, dur, EaseOutBack));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lRest, lBent, dur * 0.9f, Vary(0.08f)));
        if (hand != null) StartCoroutine(RotateBoneDelayed(hand, hRest, hTilt, dur * 0.8f, Vary(0.12f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.45f));
        // Return
        dur = Vary(0.45f);
        StartCoroutine(RotateBoneOverTime(arm, aUp, aRest, dur, SmootherStep));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lBent, lRest, dur * 0.9f, Vary(0.08f)));
        if (hand != null) StartCoroutine(RotateBoneDelayed(hand, hTilt, hRest, dur * 0.8f, Vary(0.12f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    IEnumerator GestureSmallNod()
    {
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        if (head == null) yield break;
        Quaternion hRest = head.localRotation;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;

        // Lean spine in smoothly
        Quaternion sLean = sRest * Quaternion.Euler(VaryAngle(3f), 0f, 0f);
        if (spine != null) yield return RotateBoneOverTime(spine, sRest, sLean, Vary(0.2f));

        for (int i = 0; i < 2; i++)
        {
            // Each nod varies slightly in depth and speed
            float nodDepth = VaryAngle(15f);
            float nodSpeed = Vary(0.22f);
            Quaternion down = hRest * Quaternion.Euler(nodDepth, 0f, 0f);
            yield return RotateBoneOverTime(head, hRest, down, nodSpeed, EaseInOutBack);
            yield return RotateBoneOverTime(head, down, hRest, nodSpeed * Vary(0.9f), SmootherStep);
        }

        // Lean spine back out smoothly
        if (spine != null) yield return RotateBoneOverTime(spine, sLean, sRest, Vary(0.2f));
    }

    IEnumerator GestureLeanForward()
    {
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        if (spine == null) yield break;
        Quaternion sRest = spine.localRotation;
        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;

        Quaternion sLean = sRest * Quaternion.Euler(VaryAngle(12f), 0f, 0f);
        Quaternion hUp = hRest * Quaternion.Euler(VaryAngle(-8f), 0f, 0f);

        // Lean in: spine leads, head follows slightly after
        float dur = Vary(0.45f);
        StartCoroutine(RotateBoneOverTime(spine, sRest, sLean, dur, EaseInOutBack));
        if (head != null) StartCoroutine(RotateBoneDelayed(head, hRest, hUp, dur * 0.8f, Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.5f));
        // Return together
        dur = Vary(0.45f);
        StartCoroutine(RotateBoneOverTime(spine, sLean, sRest, dur, SmootherStep));
        if (head != null) StartCoroutine(RotateBoneDelayed(head, hUp, hRest, dur * 0.8f, Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    IEnumerator GestureHandOut()
    {
        bool useRight = Random.value > 0.5f;
        Transform arm = poseManager?.GetBone(useRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        Transform lower = poseManager?.GetBone(useRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        Transform hand = poseManager?.GetBone(useRight ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (arm == null) yield break;

        Quaternion aRest = arm.localRotation;
        Quaternion lRest = lower != null ? lower.localRotation : Quaternion.identity;
        Quaternion hRest = hand != null ? hand.localRotation : Quaternion.identity;

        float side = useRight ? -1f : 1f;
        Quaternion aOut = aRest * Quaternion.Euler(VaryAngle(-35f), VaryAngle(10f) * side, VaryAngle(20f) * side);
        Quaternion lStraight = lRest * Quaternion.Euler(VaryAngle(-20f), 0f, 0f);
        Quaternion hFlat = hRest * Quaternion.Euler(VaryAngle(-25f), 0f, 0f);

        // Extend: arm leads, elbow and hand cascade
        float dur = Vary(0.42f);
        StartCoroutine(RotateBoneOverTime(arm, aRest, aOut, dur, EaseOutBack));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lRest, lStraight, dur * 0.84f, Vary(0.08f)));
        if (hand != null) StartCoroutine(RotateBoneDelayed(hand, hRest, hFlat, dur * 0.76f, Vary(0.12f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.5f));
        // Return together
        dur = Vary(0.42f);
        StartCoroutine(RotateBoneOverTime(arm, aOut, aRest, dur, SmootherStep));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lStraight, lRest, dur * 0.84f, Vary(0.08f)));
        if (hand != null) StartCoroutine(RotateBoneDelayed(hand, hFlat, hRest, dur * 0.76f, Vary(0.12f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    IEnumerator GestureShoulderLift()
    {
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        if (leftArm == null || rightArm == null) yield break;

        Quaternion lRest = leftArm.localRotation;
        Quaternion rRest = rightArm.localRotation;
        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;

        Quaternion lLift = lRest * Quaternion.Euler(VaryAngle(-12f), 0f, VaryAngle(-8f));
        Quaternion rLift = rRest * Quaternion.Euler(VaryAngle(-12f), 0f, VaryAngle(8f));
        Quaternion hTilt = hRest * Quaternion.Euler(0f, 0f, VaryAngle(8f));

        // All bones lift together with slight cascade
        float dur = Vary(0.38f);
        StartCoroutine(RotateBoneOverTime(leftArm, lRest, lLift, dur));
        StartCoroutine(RotateBoneDelayed(rightArm, rRest, rLift, dur * 0.92f, Vary(0.04f)));
        if (head != null) StartCoroutine(RotateBoneDelayed(head, hRest, hTilt, dur * 0.79f, Vary(0.08f)));
        StartCoroutine(MoveOverTime(Vector3.up, 0.02f, dur + 0.02f));
        yield return new WaitForSeconds(dur + 0.07f);
        yield return new WaitForSeconds(Vary(0.4f));
        // All return together
        dur = Vary(0.4f);
        StartCoroutine(MoveOverTime(Vector3.up, -0.02f, dur));
        if (head != null) StartCoroutine(RotateBoneOverTime(head, hTilt, hRest, dur * 0.75f));
        StartCoroutine(RotateBoneDelayed(rightArm, rLift, rRest, dur * 0.88f, Vary(0.04f)));
        StartCoroutine(RotateBoneOverTime(leftArm, lLift, lRest, dur));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    IEnumerator GestureHeadTurn()
    {
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        if (head == null) yield break;
        Quaternion hRest = head.localRotation;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;

        float dir = Random.value > 0.5f ? 1f : -1f;
        Quaternion hTurn = hRest * Quaternion.Euler(0f, VaryAngle(22f) * dir, 0f);
        Quaternion sTurn = sRest * Quaternion.Euler(0f, VaryAngle(5f) * dir, 0f);

        // Turn: head leads, spine follows slightly
        float dur = Vary(0.55f);
        StartCoroutine(RotateBoneOverTime(head, hRest, hTurn, dur));
        if (spine != null) StartCoroutine(RotateBoneDelayed(spine, sRest, sTurn, dur * 0.73f, Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.55f));
        // Return together
        dur = Vary(0.6f);
        StartCoroutine(RotateBoneOverTime(head, hTurn, hRest, dur));
        if (spine != null) StartCoroutine(RotateBoneDelayed(spine, sTurn, sRest, dur * 0.67f, Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    IEnumerator GestureBothHandsOut()
    {
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform leftHand = poseManager?.GetBone(HumanBodyBones.LeftHand);
        Transform rightHand = poseManager?.GetBone(HumanBodyBones.RightHand);
        if (leftArm == null || rightArm == null) yield break;

        Quaternion lRest = leftArm.localRotation;
        Quaternion rRest = rightArm.localRotation;
        Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
        Quaternion lhRest = leftHand != null ? leftHand.localRotation : Quaternion.identity;
        Quaternion rhRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;

        // Both arms come out in an "explaining" pose
        Quaternion lOut = lRest * Quaternion.Euler(VaryAngle(-30f), 0f, VaryAngle(12f));
        Quaternion rOut = rRest * Quaternion.Euler(VaryAngle(-30f), 0f, VaryAngle(-12f));
        Quaternion llBent = llRest * Quaternion.Euler(VaryAngle(-35f), 0f, 0f);
        Quaternion rlBent = rlRest * Quaternion.Euler(VaryAngle(-35f), 0f, 0f);
        Quaternion lhUp = lhRest * Quaternion.Euler(VaryAngle(-20f), 0f, 0f);
        Quaternion rhUp = rhRest * Quaternion.Euler(VaryAngle(-20f), 0f, 0f);

        // All arms rise together with cascade
        float dur = Vary(0.5f);
        StartCoroutine(RotateBoneOverTime(leftArm, lRest, lOut, dur));
        StartCoroutine(RotateBoneDelayed(rightArm, rRest, rOut, dur * 0.96f, Vary(0.04f)));
        if (leftLower != null) StartCoroutine(RotateBoneDelayed(leftLower, llRest, llBent, dur * 0.8f, Vary(0.1f)));
        if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlRest, rlBent, dur * 0.8f, Vary(0.12f)));
        if (leftHand != null) StartCoroutine(RotateBoneDelayed(leftHand, lhRest, lhUp, dur * 0.7f, Vary(0.15f)));
        if (rightHand != null) StartCoroutine(RotateBoneDelayed(rightHand, rhRest, rhUp, dur * 0.7f, Vary(0.15f)));
        yield return new WaitForSeconds(dur + 0.05f);

        yield return new WaitForSeconds(Vary(0.55f));

        // All return together with cascade
        dur = Vary(0.5f);
        StartCoroutine(RotateBoneOverTime(leftArm, lOut, lRest, dur));
        StartCoroutine(RotateBoneDelayed(rightArm, rOut, rRest, dur * 0.96f, Vary(0.04f)));
        if (leftLower != null) StartCoroutine(RotateBoneDelayed(leftLower, llBent, llRest, dur * 0.8f, Vary(0.08f)));
        if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlBent, rlRest, dur * 0.8f, Vary(0.1f)));
        if (leftHand != null) StartCoroutine(RotateBoneDelayed(leftHand, lhUp, lhRest, dur * 0.7f, Vary(0.12f)));
        if (rightHand != null) StartCoroutine(RotateBoneDelayed(rightHand, rhUp, rhRest, dur * 0.7f, Vary(0.12f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    IEnumerator GestureHandToChest()
    {
        bool useRight = Random.value > 0.5f;
        Transform arm = poseManager?.GetBone(useRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        Transform lower = poseManager?.GetBone(useRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        if (arm == null) yield break;

        Quaternion aRest = arm.localRotation;
        Quaternion lRest = lower != null ? lower.localRotation : Quaternion.identity;

        float side = useRight ? -1f : 1f;
        Quaternion aChest = aRest * Quaternion.Euler(VaryAngle(-25f), VaryAngle(15f) * side, VaryAngle(10f) * side);
        Quaternion lBent = lRest * Quaternion.Euler(VaryAngle(-60f), 0f, 0f);

        // Arm and elbow move together toward chest
        float dur = Vary(0.5f);
        StartCoroutine(RotateBoneOverTime(arm, aRest, aChest, dur));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lRest, lBent, dur * 0.84f, Vary(0.08f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.5f));
        // Return together
        dur = Vary(0.5f);
        StartCoroutine(RotateBoneOverTime(arm, aChest, aRest, dur));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lBent, lRest, dur * 0.84f, Vary(0.08f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    IEnumerator GestureBodyShift()
    {
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform hips = poseManager?.GetBone(HumanBodyBones.Hips);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        if (spine == null) yield break;

        Quaternion sRest = spine.localRotation;
        Quaternion hipRest = hips != null ? hips.localRotation : Quaternion.identity;
        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;

        float dir = Random.value > 0.5f ? 1f : -1f;
        Quaternion sShift = sRest * Quaternion.Euler(0f, 0f, VaryAngle(8f) * dir);
        Quaternion hipShift = hipRest * Quaternion.Euler(0f, VaryAngle(4f) * dir, 0f);
        Quaternion hCompensate = hRest * Quaternion.Euler(0f, 0f, VaryAngle(-5f) * dir);

        // Shift: spine leads, hips and head follow
        float dur = Vary(0.45f);
        StartCoroutine(RotateBoneOverTime(spine, sRest, sShift, dur));
        if (hips != null) StartCoroutine(RotateBoneDelayed(hips, hipRest, hipShift, dur * 0.78f, Vary(0.08f)));
        if (head != null) StartCoroutine(RotateBoneDelayed(head, hRest, hCompensate, dur * 0.67f, Vary(0.12f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.6f));
        // Return together
        dur = Vary(0.45f);
        StartCoroutine(RotateBoneOverTime(spine, sShift, sRest, dur));
        if (hips != null) StartCoroutine(RotateBoneDelayed(hips, hipShift, hipRest, dur * 0.78f, Vary(0.08f)));
        if (head != null) StartCoroutine(RotateBoneDelayed(head, hCompensate, hRest, dur * 0.67f, Vary(0.12f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    /// <summary> Quick small head shake — for disagreement/negation during speech </summary>
    IEnumerator GestureSmallHeadShake()
    {
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        if (head == null) yield break;
        Quaternion rest = head.localRotation;

        for (int i = 0; i < 2; i++)
        {
            float amp = VaryAngle(15f);
            float spd = Vary(0.12f);
            Quaternion left = rest * Quaternion.Euler(0f, -amp, 0f);
            Quaternion right = rest * Quaternion.Euler(0f, amp, 0f);
            yield return RotateBoneOverTime(head, rest, left, spd);
            yield return RotateBoneOverTime(head, left, right, spd * 2f);
            yield return RotateBoneOverTime(head, right, rest, spd);
        }
    }

    /// <summary> Arm extends forward in a pointing gesture — explaining or directing attention </summary>
    IEnumerator GestureFingerPoint()
    {
        bool useRight = Random.value > 0.4f; // favour right hand
        Transform arm   = poseManager?.GetBone(useRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        Transform lower = poseManager?.GetBone(useRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        Transform hand  = poseManager?.GetBone(useRight ? HumanBodyBones.RightHand     : HumanBodyBones.LeftHand);
        if (arm == null) yield break;

        Quaternion aRest = arm.localRotation;
        Quaternion lRest = lower != null ? lower.localRotation : Quaternion.identity;
        Quaternion hRest = hand  != null ? hand.localRotation  : Quaternion.identity;

        float side = useRight ? -1f : 1f;
        // Arm extends forward and slightly out, elbow less bent, wrist nearly straight
        Quaternion aPoint = aRest  * Quaternion.Euler(VaryAngle(-50f), VaryAngle(8f) * side, VaryAngle(22f) * side);
        Quaternion lPoint = lRest  * Quaternion.Euler(VaryAngle(-30f), 0f, 0f);
        Quaternion hPoint = hRest  * Quaternion.Euler(VaryAngle(-15f), 0f, 0f);

        float dur = Vary(0.38f);
        StartCoroutine(RotateBoneOverTime(arm, aRest, aPoint, dur, EaseOutBack));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lRest, lPoint, dur * 0.85f, Vary(0.07f)));
        if (hand  != null) StartCoroutine(RotateBoneDelayed(hand,  hRest, hPoint, dur * 0.75f, Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.45f));
        dur = Vary(0.4f);
        StartCoroutine(RotateBoneOverTime(arm, aPoint, aRest, dur, SmootherStep));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lPoint, lRest, dur * 0.85f, Vary(0.07f)));
        if (hand  != null) StartCoroutine(RotateBoneDelayed(hand,  hPoint, hRest, dur * 0.75f, Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    /// <summary> Rhythmic small head bobs — natural beat-keeping during speech </summary>
    IEnumerator GestureHeadBob()
    {
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        if (head == null) yield break;
        Quaternion rest = head.localRotation;
        int bobs = Random.Range(2, 4);
        for (int i = 0; i < bobs; i++)
        {
            float depth = VaryAngle(12f);
            float spd   = Vary(0.14f);
            Quaternion down = rest * Quaternion.Euler(depth, 0f, 0f);
            yield return RotateBoneOverTime(head, rest, down, spd, EaseInOutBack);
            yield return RotateBoneOverTime(head, down, rest, spd * Vary(0.88f), SmootherStep);
            yield return new WaitForSeconds(Vary(0.06f));
        }
    }

    /// <summary> Hand lifts to chin height with head lean — thoughtful listening gesture </summary>
    IEnumerator GestureChin()
    {
        bool useRight = Random.value > 0.5f;
        Transform arm   = poseManager?.GetBone(useRight ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        Transform lower = poseManager?.GetBone(useRight ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        Transform head  = poseManager?.GetBone(HumanBodyBones.Head);
        if (arm == null) yield break;

        Quaternion aRest = arm.localRotation;
        Quaternion lRest = lower != null ? lower.localRotation : Quaternion.identity;
        Quaternion hRest = head  != null ? head.localRotation  : Quaternion.identity;

        float side = useRight ? -1f : 1f;
        Quaternion aChin = aRest * Quaternion.Euler(VaryAngle(-55f), VaryAngle(15f) * side, VaryAngle(-28f) * side);
        Quaternion lBent = lRest * Quaternion.Euler(VaryAngle(-80f), 0f, 0f);
        Quaternion hTilt = hRest * Quaternion.Euler(VaryAngle(3f),   0f, VaryAngle(-7f) * side);

        float dur = Vary(0.5f);
        StartCoroutine(RotateBoneOverTime(arm, aRest, aChin, dur));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lRest, lBent, dur * 0.86f, Vary(0.08f)));
        if (head  != null) StartCoroutine(RotateBoneDelayed(head,  hRest, hTilt, dur * 0.7f,  Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
        yield return new WaitForSeconds(Vary(0.55f));
        dur = Vary(0.5f);
        StartCoroutine(RotateBoneOverTime(arm, aChin, aRest, dur));
        if (lower != null) StartCoroutine(RotateBoneDelayed(lower, lBent, lRest, dur * 0.86f, Vary(0.08f)));
        if (head  != null) StartCoroutine(RotateBoneDelayed(head,  hTilt, hRest, dur * 0.7f,  Vary(0.1f)));
        yield return new WaitForSeconds(dur + 0.05f);
    }

    // ===================== Full Emote System =====================
    /// <summary>
    /// Trigger a full emote animation by keyword.
    /// Interrupts any current talking gesture. Accepts any text — aggressive fuzzy matching.
    /// </summary>
    public void PlayEmote(string emote)
    {
        if (isPlaying) return; // Don't stack full emotes

        // Interrupt any active talking gesture immediately
        if (isGesturing)
        {
            isGesturing = false;
            if (poseManager != null) poseManager.ResetToRestPose();
        }

        string lower = emote.ToLower().Trim();
        Debug.Log($"EmoteAnimator.PlayEmote: '{lower}'");

        // Reserved style: intercept high-energy emotes before standard keyword chain
        if (style == CharacterStyle.Reserved && TryPlayReservedEmote(lower))
        {
            var faceAnimR = GetComponent<FaceAnimator>();
            if (faceAnimR != null) faceAnimR.SetEmotionFromEmote(lower);
            return;
        }

        // Map keywords to animations — ordered from specific to general
        // Bone-heavy animations listed first for priority

        // HEAD BONE animations
        if (lower.Contains("nod") || lower.Contains("agree") || lower.Contains("yes") || lower.Contains("mhm"))
            StartCoroutine(DoNod());
        else if (lower.Contains("shake") && (lower.Contains("head") || lower.Contains("no"))
            || lower.Contains("disagree") || lower.Contains("nuh") || lower.Contains("nope"))
            StartCoroutine(DoHeadShake());
        else if (lower.Contains("think") || lower.Contains("ponder") || lower.Contains("hmm")
            || lower.Contains("wonder") || lower.Contains("curious") || lower.Contains("consider"))
            StartCoroutine(DoThinking());
        else if (lower.Contains("tilt") || lower.Contains("cock") || lower.Contains("huh"))
            StartCoroutine(DoTiltSide());
        else if (lower.Contains("look away") || lower.Contains("avoid") || lower.Contains("glance")
            || lower.Contains("turn away") || lower.Contains("avert"))
            StartCoroutine(DoLookAway());

        // ARM BONE animations
        else if (lower.Contains("big wave") || lower.Contains("wave both") || lower.Contains("waves both")
            || lower.Contains("wave arms") || lower.Contains("waves arms") || lower.Contains("flail"))
            StartCoroutine(DoBigWave());
        else if (lower.Contains("wave") || lower.Contains("greet") || lower.Contains("hello")
            || lower.Contains("bye") || lower.Contains("hi ") || lower == "hi")
            StartCoroutine(DoWave());
        else if (lower.Contains("point") || lower.Contains("look at") || lower.Contains("check")
            || lower.Contains("see") || lower.Contains("there") || lower.Contains("that"))
            StartCoroutine(DoPoint());
        else if (lower.Contains("cross") || lower.Contains("fold") || lower.Contains("arms")
            || lower.Contains("stern") || lower.Contains("serious") || lower.Contains("wait"))
            StartCoroutine(DoCrossArms());
        else if (lower.Contains("raise") || lower.Contains("hands up") || lower.Contains("celebrat")
            || lower.Contains("hooray") || lower.Contains("woohoo") || lower.Contains("victory")
            || lower.Contains("cheer"))
            StartCoroutine(DoHandsUp());
        else if (lower.Contains("clap") || lower.Contains("applaud") || lower.Contains("bravo")
            || lower.Contains("amazing") || lower.Contains("great job") || lower.Contains("nice"))
            StartCoroutine(DoClap());
        else if (lower.Contains("shrug") || lower.Contains("dunno") || lower.Contains("don't know")
            || lower.Contains("idk") || lower.Contains("whatever"))
            StartCoroutine(DoShrug());
        else if (lower.Contains("stretch") || lower.Contains("yawn") || lower.Contains("tired")
            || lower.Contains("sleepy") || lower.Contains("wak"))
            StartCoroutine(DoStretch());
        else if (lower.Contains("facepalm") || lower.Contains("face palm") || lower.Contains("cringe")
            || lower.Contains("smh") || lower.Contains("oh no"))
            StartCoroutine(DoFacepalm());
        else if (lower.Contains("chin") || lower.Contains("rest") || lower.Contains("contemplate"))
            StartCoroutine(DoChinRest());
        else if (lower.Contains("pat") || lower.Contains("comfort") || lower.Contains("hug")
            || lower.Contains("lean") || lower.Contains("close"))
            StartCoroutine(DoGentle());
        else if (lower.Contains("bend") || lower.Contains("bow") || lower.Contains("forward")
            || lower.Contains("lean forward") || lower.Contains("stoop") || lower.Contains("double over")
            || lower.Contains("hunch") || lower.Contains("slump") || lower.Contains("droop"))
            StartCoroutine(DoBendOver());
        else if (lower.Contains("recoil") || lower.Contains("stumble") || lower.Contains("stagger")
            || lower.Contains("flinch") || lower.Contains("jolt") || lower.Contains("startle"))
            StartCoroutine(DoRecoil());

        // SPINE + FULL BODY bone animations
        else if (lower.Contains("giggle") || lower.Contains("laugh") || lower.Contains("chuckle")
            || lower.Contains("haha") || lower.Contains("lol") || lower.Contains("hehe")
            || lower.Contains("snicker") || lower.Contains("amuse"))
            StartCoroutine(DoBounce());
        else if (lower.Contains("surprise") || lower.Contains("startl") || lower.Contains("shocked"))
            StartCoroutine(DoSurprise());
        else if (lower.Contains("gasp") || lower.Contains("omg") || lower.Contains("whoa") || lower.Contains("wow")
            || lower.Contains("really") || lower.Contains("what!"))
            StartCoroutine(DoGasp());
        else if (lower.Contains("pout") || lower.Contains("sulk") || lower.Contains("grumpy")
            || lower.Contains("hmph") || lower.Contains("angry") || lower.Contains("annoy")
            || lower.Contains("frustrat") || lower.Contains("mad"))
            StartCoroutine(DoPout());
        else if (lower.Contains("happy") || lower.Contains("joyful") || lower.Contains("overjoyed"))
            StartCoroutine(DoHappy());
        else if (lower.Contains("jump") || lower.Contains("excited")
            || lower.Contains("yay") || lower.Contains("bounces") || lower.Contains("bounce"))
            StartCoroutine(DoJump());
        else if (lower.Contains("spin") || lower.Contains("twirl") || lower.Contains("turn"))
            StartCoroutine(DoSpin());
        else if (lower.Contains("dance") || lower.Contains("groove") || lower.Contains("sway")
            || lower.Contains("step") || lower.Contains("move") || lower.Contains("rhythm"))
            StartCoroutine(DoDance());
        else if (lower.Contains("peek") || lower.Contains("hide") || lower.Contains("sneak")
            || lower.Contains("shy") || lower.Contains("embarrass") || lower.Contains("blush")
            || lower.Contains("fluster"))
            StartCoroutine(DoPeek());
        else if (lower.Contains("sigh") || lower.Contains("sad") || lower.Contains("down")
            || lower.Contains("disappoint") || lower.Contains("sorry") || lower.Contains("miss"))
            StartCoroutine(DoDeflate());
        else if (lower.Contains("wiggle") || lower.Contains("fidget") || lower.Contains("nervous")
            || lower.Contains("squirm"))
            StartCoroutine(DoWiggle());
        else if (lower.Contains("smile") || lower.Contains("grin") || lower.Contains("beam")
            || lower.Contains("warm") || lower.Contains("soft"))
            StartCoroutine(DoNod()); // Warm smile = gentle nod
        else if (lower.Contains("stare") || lower.Contains("blink") || lower.Contains("eye"))
            StartCoroutine(DoSquash());
        else
            StartCoroutine(DoBounce()); // Default: gentle bounce with head bob

        // Also trigger facial expression if FaceAnimator exists
        var faceAnim = GetComponent<FaceAnimator>();
        if (faceAnim != null)
            faceAnim.SetEmotionFromEmote(lower);
    }

    /// <summary>
    /// Play a random body animation based on the detected emotion.
    /// Called as a fallback guarantee — ensures EVERY response triggers bone movement.
    /// </summary>
    public void PlayEmotionAnimation(string emotion)
    {
        if (isPlaying) return;

        string lower = (emotion ?? "neutral").ToLower();
        int pick;

        // Reserved style (Ren): calm, understated reactions — no jumping or dancing
        if (style == CharacterStyle.Reserved)
        {
            switch (lower)
            {
                case "joy": // Restrained satisfaction
                    pick = Random.Range(0, 4);
                    switch (pick)
                    {
                        case 0: StartCoroutine(DoNod()); break;
                        case 1: StartCoroutine(DoChinRest()); break;
                        case 2: StartCoroutine(DoTiltSide()); break;
                        case 3: StartCoroutine(DoShrug()); break;
                    }
                    break;
                case "angry": // His thing — deliberate and contained
                    pick = Random.Range(0, 4);
                    switch (pick)
                    {
                        case 0: StartCoroutine(DoCrossArms()); break;
                        case 1: StartCoroutine(DoFacepalm()); break;
                        case 2: StartCoroutine(DoHeadShake()); break;
                        case 3: StartCoroutine(DoPout()); break;
                    }
                    break;
                case "sorrow": // Quiet, no drama
                    pick = Random.Range(0, 4);
                    switch (pick)
                    {
                        case 0: StartCoroutine(DoDeflate()); break;
                        case 1: StartCoroutine(DoLookAway()); break;
                        case 2: StartCoroutine(DoShrug()); break;
                        case 3: StartCoroutine(DoChinRest()); break;
                    }
                    break;
                case "fun": // Plays it cool — sneaky/smug rather than giddy
                    pick = Random.Range(0, 5);
                    switch (pick)
                    {
                        case 0: StartCoroutine(DoPeek()); break;
                        case 1: StartCoroutine(DoShrug()); break;
                        case 2: StartCoroutine(DoCrossArms()); break;
                        case 3: StartCoroutine(DoLookAway()); break;
                        case 4: StartCoroutine(DoThinking()); break;
                    }
                    break;
                default: // Neutral — composed, observant
                    pick = Random.Range(0, 5);
                    switch (pick)
                    {
                        case 0: StartCoroutine(DoNod()); break;
                        case 1: StartCoroutine(DoTiltSide()); break;
                        case 2: StartCoroutine(DoChinRest()); break;
                        case 3: StartCoroutine(DoShrug()); break;
                        case 4: StartCoroutine(DoLookAway()); break;
                    }
                    break;
            }
            return;
        }

        switch (lower)
        {
            case "joy":
                pick = Random.Range(0, 8);
                switch (pick)
                {
                    case 0: StartCoroutine(DoBounce()); break;
                    case 1: StartCoroutine(DoJump()); break;
                    case 2: StartCoroutine(DoClap()); break;
                    case 3: StartCoroutine(DoHandsUp()); break;
                    case 4: StartCoroutine(DoDance()); break;
                    case 5: StartCoroutine(DoBigWave()); break;
                    case 6: StartCoroutine(DoWave()); break;
                    case 7: StartCoroutine(DoHappy()); break;
                }
                break;
            case "angry":
                pick = Random.Range(0, 5);
                switch (pick)
                {
                    case 0: StartCoroutine(DoCrossArms()); break;
                    case 1: StartCoroutine(DoHeadShake()); break;
                    case 2: StartCoroutine(DoPout()); break;
                    case 3: StartCoroutine(DoFacepalm()); break;
                    case 4: StartCoroutine(DoRecoil()); break;
                }
                break;
            case "sorrow":
                pick = Random.Range(0, 7);
                switch (pick)
                {
                    case 0: StartCoroutine(DoDeflate()); break;
                    case 1: StartCoroutine(DoLookAway()); break;
                    case 2: StartCoroutine(DoGentle()); break;
                    case 3: StartCoroutine(DoShrug()); break;
                    case 4: StartCoroutine(DoBendOver()); break;
                    case 5: StartCoroutine(DoPeek()); break;
                    case 6: StartCoroutine(DoThinking()); break;
                }
                break;
            case "fun":
                pick = Random.Range(0, 9);
                switch (pick)
                {
                    case 0: StartCoroutine(DoWiggle()); break;
                    case 1: StartCoroutine(DoSpin()); break;
                    case 2: StartCoroutine(DoPeek()); break;
                    case 3: StartCoroutine(DoWave()); break;
                    case 4: StartCoroutine(DoDance()); break;
                    case 5: StartCoroutine(DoBigWave()); break;
                    case 6: StartCoroutine(DoBounce()); break;
                    case 7: StartCoroutine(DoSurprise()); break;
                    case 8: StartCoroutine(DoHappy()); break;
                }
                break;
            default: // neutral
                pick = Random.Range(0, 8);
                switch (pick)
                {
                    case 0: StartCoroutine(DoNod()); break;
                    case 1: StartCoroutine(DoTiltSide()); break;
                    case 2: StartCoroutine(DoChinRest()); break;
                    case 3: StartCoroutine(DoPoint()); break;
                    case 4: StartCoroutine(DoShrug()); break;
                    case 5: StartCoroutine(DoBendOver()); break;
                    case 6: StartCoroutine(DoWave()); break;
                    case 7: StartCoroutine(DoThinking()); break;
                }
                break;
        }
    }

    // --- Emote Animations ---

    /// <summary> Tilt head back with arm raise (yawn) — bone-based </summary>
    IEnumerator DoTiltBack()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
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

    /// <summary> Quick nods using head bone + spine lean + hand gesture </summary>
    IEnumerator DoNod()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        if (head != null)
        {
            Quaternion hRest = head.localRotation;
            Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
            Quaternion aRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
            Quaternion lRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;

            // Lean forward + small hand gesture — all staggered so they arrive together
            Quaternion sLean    = sRest * Quaternion.Euler(Vary(8f), 0f, 0f);
            Quaternion aGesture = aRest * Quaternion.Euler(VaryAngle(-20f), 0f, VaryAngle(-12f));
            Quaternion lBend    = lRest * Quaternion.Euler(VaryAngle(-30f), 0f, 0f);

            float prepDur = Vary(0.2f);
            if (spine     != null) StartCoroutine(RotateBoneOverTime(spine,      sRest,  sLean,    prepDur,        SmootherStep));
            if (rightArm  != null) StartCoroutine(RotateBoneDelayed(rightArm,  aRest,  aGesture, prepDur * 0.9f, 0.03f));
            if (rightLower!= null) StartCoroutine(RotateBoneDelayed(rightLower, lRest,  lBend,    prepDur * 0.8f, 0.06f));
            yield return new WaitForSeconds(prepDur);

            for (int i = 0; i < 2; i++)
            {
                float nodDepth = VaryAngle(22f);
                float nodSpd   = Vary(0.13f);
                Quaternion down = hRest * Quaternion.Euler(nodDepth, 0f, 0f);
                yield return RotateBoneOverTime(head, hRest, down, nodSpd,          EaseInOutBack);
                yield return RotateBoneOverTime(head, down,  hRest, nodSpd * 0.85f, EaseInOutBack);
            }

            // Return all in parallel
            float retDur = Vary(0.22f);
            if (spine     != null) StartCoroutine(RotateBoneOverTime(spine,      sLean,    sRest,  retDur,        SmootherStep));
            if (rightArm  != null) StartCoroutine(RotateBoneDelayed(rightArm,  aGesture, aRest,  retDur * 0.9f, 0.02f));
            if (rightLower!= null) StartCoroutine(RotateBoneDelayed(rightLower, lBend,    lRest,  retDur * 0.8f, 0.04f));
            yield return new WaitForSeconds(retDur);
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                yield return RotateOverTime(Vector3.right, 15f, 0.15f);
                yield return RotateOverTime(Vector3.right, -15f, 0.15f);
            }
        }

        ResetState();
    }

    /// <summary> Wave with actual arm bone + head tilt + body lean — dramatic </summary>
    IEnumerator DoWave()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform rightHand = poseManager?.GetBone(HumanBodyBones.RightHand);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        if (rightArm != null)
        {
            Quaternion armRest   = rightArm.localRotation;
            Quaternion lowerRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion handRest  = rightHand  != null ? rightHand.localRotation  : Quaternion.identity;
            Quaternion headRest  = head  != null ? head.localRotation  : Quaternion.identity;
            Quaternion spineRest = spine != null ? spine.localRotation : Quaternion.identity;

            // CONFIRMED bone axes (from diagnostic):
            //   UpperArm +X → DOWN   ∴ -X raises the arm UP
            //   UpperArm +Y → LEFT   ∴ 0 Y keeps elbow pointing out to the side (natural wave)
            //   UpperArm +Z → BACK   ∴ roll axis — not used
            //
            // Goal: arm raised to side, elbow bent OUTWARD, forearm up, palm facing viewer.
            Quaternion armRaised = armRest * Quaternion.Euler(-90f, 0f, 0f);
            Quaternion lowerBent = lowerRest * Quaternion.Euler(-80f, 0f, 0f);
            Quaternion headTilt  = headRest  * Quaternion.Euler(0f, -12f, 0f);
            Quaternion spineLean = spineRest * Quaternion.Euler(0f,  0f,  -3f);
            // Hand oscillates purely between A and B — no mid-cycle return to rest.
            Quaternion handWaveA = handRest * Quaternion.Euler( 30f, 0f, 0f);
            Quaternion handWaveB = handRest * Quaternion.Euler(-30f, 0f, 0f);

            // ── Phase 1: Anticipation windup (tiny drop before the snap up) ──────────
            Quaternion armWindup = armRest * Quaternion.Euler(8f, 0f, 0f);
            yield return RotateBoneOverTime(rightArm, armRest, armWindup, 0.06f, SmootherStep);

            // ── Phase 2: Arm snaps up with EaseOutBack overshoot ─────────────────────
            //    While the arm is still rising, start elbow + head + spine in parallel
            //    so every body part doesn't start/stop at the same frame.
            float raiseDur = Vary(0.25f);
            if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, lowerRest, lowerBent, raiseDur * 0.9f, 0.04f));
            if (head  != null)      StartCoroutine(RotateBoneDelayed(head,       headRest,  headTilt,  raiseDur * 0.85f, 0.06f));
            if (spine != null)      StartCoroutine(RotateBoneDelayed(spine,      spineRest, spineLean, raiseDur * 0.8f,  0.05f));
            yield return RotateBoneOverTime(rightArm, armWindup, armRaised, raiseDur, EaseOutBack);

            // ── Phase 3: Wave — palm rocks A↔B, pure oscillation, no rest-snap ───────
            //    Each half-swing uses EaseOutElastic so the palm has a springy snap.
            float halfSwing = Vary(0.13f);
            bool toA = true;
            for (int i = 0; i < 6; i++)   // 6 half-swings = 3 full cycles
            {
                if (rightHand != null)
                {
                    Quaternion waveFrom = toA ? handRest  : handWaveA;
                    Quaternion waveTo   = toA ? handWaveA : handWaveB;
                    // Alternate last swing back to rest so we land cleanly
                    if (i == 5) waveTo = handRest;
                    yield return RotateBoneOverTime(rightHand, waveFrom, waveTo,
                                                    i == 0 ? halfSwing * 0.7f : halfSwing,
                                                    EaseOutElastic);
                    toA = !toA;
                }
            }

            // ── Phase 4: Lower everything simultaneously with SmootherStep ────────────
            float lowerDur = Vary(0.32f);
            if (rightHand  != null) StartCoroutine(RotateBoneOverTime(rightHand,  handRest,  handRest,  0.01f, SmootherStep)); // ensure landed
            if (rightLower != null) StartCoroutine(RotateBoneOverTime(rightLower, lowerBent, lowerRest, lowerDur * 0.85f, SmootherStep));
            if (head  != null)      StartCoroutine(RotateBoneOverTime(head,       headTilt,  headRest,  lowerDur * 0.9f,  SmootherStep));
            if (spine != null)      StartCoroutine(RotateBoneOverTime(spine,      spineLean, spineRest, lowerDur * 0.75f, SmootherStep));
            yield return RotateBoneOverTime(rightArm, armRaised, armRest, lowerDur, SmootherStep);
        }
        else
        {
            float angle = 22f;
            float speed = 0.15f;
            for (int i = 0; i < 4; i++)
            {
                yield return RotateOverTime(Vector3.forward, angle, speed);
                yield return RotateOverTime(Vector3.forward, -angle * 2f, speed * 2f);
                yield return RotateOverTime(Vector3.forward, angle, speed);
            }
        }

        ResetState();
    }

    /// <summary> Bouncy giggle — head bob + spine bounce + arms flap — dramatic bone-based </summary>
    IEnumerator DoBounce()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftHand = poseManager?.GetBone(HumanBodyBones.LeftHand);
        Transform rightHand = poseManager?.GetBone(HumanBodyBones.RightHand);

        float bounceHeight = 0.12f;
        float speed = 0.1f;

        Quaternion headRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion spineRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion lArmRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rArmRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion lHandRest = leftHand != null ? leftHand.localRotation : Quaternion.identity;
        Quaternion rHandRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;

        for (int i = 0; i < 5; i++)
        {
            // Target rotations for this bounce (alternating sides)
            Quaternion headTarget = headRest * Quaternion.Euler(-15f, (i % 2 == 0 ? 10f : -10f), (i % 2 == 0 ? 8f : -8f));
            Quaternion spineTarget = spineRest * Quaternion.Euler(-6f, 0f, (i % 2 == 0 ? 8f : -8f));
            Quaternion lArmTarget = lArmRest * Quaternion.Euler(-25f, 0f, -20f);
            Quaternion rArmTarget = rArmRest * Quaternion.Euler(-25f, 0f, 20f);

            // Bounce up while smoothly blending bones to peak pose
            float upElapsed = 0f;
            Vector3 upStart = transform.localPosition;
            Vector3 upEnd = upStart + Vector3.up * bounceHeight;
            while (upElapsed < speed)
            {
                upElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, upElapsed / speed);
                transform.localPosition = Vector3.Lerp(upStart, upEnd, t);
                // Blend bones in parallel
                if (head != null) head.localRotation = Quaternion.Slerp(headRest, headTarget, t);
                if (spine != null) spine.localRotation = Quaternion.Slerp(spineRest, spineTarget, t);
                if (leftArm != null) leftArm.localRotation = Quaternion.Slerp(lArmRest, lArmTarget, t);
                if (rightArm != null) rightArm.localRotation = Quaternion.Slerp(rArmRest, rArmTarget, t);
                yield return null;
            }

            // Bounce down while smoothly blending bones back to rest
            float downElapsed = 0f;
            Vector3 downStart = transform.localPosition;
            Vector3 downEnd = downStart - Vector3.up * bounceHeight;
            while (downElapsed < speed)
            {
                downElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, downElapsed / speed);
                transform.localPosition = Vector3.Lerp(downStart, downEnd, t);
                if (head != null) head.localRotation = Quaternion.Slerp(headTarget, headRest, t);
                if (spine != null) spine.localRotation = Quaternion.Slerp(spineTarget, spineRest, t);
                if (leftArm != null) leftArm.localRotation = Quaternion.Slerp(lArmTarget, lArmRest, t);
                if (rightArm != null) rightArm.localRotation = Quaternion.Slerp(rArmTarget, rArmRest, t);
                yield return null;
            }
        }

        ResetState();
    }

    /// <summary> Deflate with head drop + spine curl + arms droop — full bone sadness </summary>
    IEnumerator DoDeflate()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform chest = poseManager?.GetBone(HumanBodyBones.Chest);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftHand = poseManager?.GetBone(HumanBodyBones.LeftHand);
        Transform rightHand = poseManager?.GetBone(HumanBodyBones.RightHand);

        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion cRest = chest != null ? chest.localRotation : Quaternion.identity;
        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion lhRest = leftHand != null ? leftHand.localRotation : Quaternion.identity;
        Quaternion rhRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;

        // Slump: head drops forward, spine curls, chest caves, arms hang limp
        Quaternion hDrop = hRest * Quaternion.Euler(25f, 0f, 5f);
        Quaternion sCurl = sRest * Quaternion.Euler(18f, 0f, 0f);
        Quaternion cCave = cRest * Quaternion.Euler(10f, 0f, 0f);
        Quaternion lDroop = lRest * Quaternion.Euler(15f, 0f, 5f);
        Quaternion rDroop = rRest * Quaternion.Euler(15f, 0f, -5f);
        Quaternion lhDangle = lhRest * Quaternion.Euler(20f, 0f, 0f);
        Quaternion rhDangle = rhRest * Quaternion.Euler(20f, 0f, 0f);

        // Sink: everything slumps together with staggered cascade — head last (it droops)
        float slumpDur = Vary(0.4f);
        StartCoroutine(ScaleOverTime(originalScale * 0.88f, slumpDur));
        if (spine    != null) StartCoroutine(RotateBoneOverTime(spine,    sRest,  sCurl,   slumpDur,         SmootherStep));
        if (chest    != null) StartCoroutine(RotateBoneDelayed(chest,    cRest,  cCave,   slumpDur * 0.85f, 0.05f));
        if (leftArm  != null) StartCoroutine(RotateBoneDelayed(leftArm,  lRest,  lDroop,  slumpDur * 0.78f, 0.07f));
        if (rightArm != null) StartCoroutine(RotateBoneDelayed(rightArm, rRest,  rDroop,  slumpDur * 0.78f, 0.09f));
        if (head     != null) StartCoroutine(RotateBoneDelayed(head,     hRest,  hDrop,   slumpDur * 0.7f,  0.12f));
        yield return new WaitForSeconds(slumpDur);

        yield return new WaitForSeconds(Vary(0.75f));

        // Recover — all in parallel, head leads the lift
        float recoverDur = Vary(0.38f);
        StartCoroutine(ScaleOverTime(originalScale, recoverDur));
        if (head     != null) StartCoroutine(RotateBoneOverTime(head,     hDrop,  hRest,   recoverDur,         EaseOutBack));
        if (leftArm  != null) StartCoroutine(RotateBoneDelayed(leftArm,  lDroop, lRest,   recoverDur * 0.88f, 0.03f));
        if (rightArm != null) StartCoroutine(RotateBoneDelayed(rightArm, rDroop, rRest,   recoverDur * 0.88f, 0.04f));
        if (chest    != null) StartCoroutine(RotateBoneDelayed(chest,    cCave,  cRest,   recoverDur * 0.78f, 0.06f));
        if (spine    != null) StartCoroutine(RotateBoneDelayed(spine,    sCurl,  sRest,   recoverDur * 0.7f,  0.1f));
        yield return new WaitForSeconds(recoverDur);

        ResetState();
    }

    /// <summary> Arms-up stretch using bones </summary>
    IEnumerator DoStretch()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
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

            // Raise arms simultaneously — left leads by one frame, right follows, spine+head cascade
            float stretchUp = Vary(0.38f);
            StartCoroutine(RotateBoneOverTime(leftArm,  lRest,  lUp,   stretchUp,         EaseInOutBack));
            StartCoroutine(RotateBoneDelayed(rightArm,  rRest,  rUp,   stretchUp * 0.94f, 0.02f));
            if (spine != null) StartCoroutine(RotateBoneDelayed(spine, sRest, sArch, stretchUp * 0.75f, 0.07f));
            if (head  != null) StartCoroutine(RotateBoneDelayed(head,  hRest, hBack, stretchUp * 0.65f, 0.1f));
            yield return new WaitForSeconds(stretchUp);

            yield return new WaitForSeconds(Vary(0.55f));

            // Lower back — all in parallel
            float stretchDown = Vary(0.36f);
            if (head  != null) StartCoroutine(RotateBoneOverTime(head,  hBack,  hRest,  stretchDown * 0.7f, SmootherStep));
            if (spine != null) StartCoroutine(RotateBoneDelayed(spine,  sArch,  sRest,  stretchDown * 0.8f, 0.04f));
            StartCoroutine(RotateBoneDelayed(rightArm, rUp,    rRest,  stretchDown * 0.94f, 0.02f));
            yield return RotateBoneOverTime(leftArm, lUp, lRest, stretchDown, SmootherStep);
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

    /// <summary> Excited jump with arms raising + head back + spine arch </summary>
    IEnumerator DoJump()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        float jumpHeight = 0.25f;
        float upSpeed = 0.08f;
        float downSpeed = 0.06f;

        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;

        // Arms up poses for peak of jump
        Quaternion lUp = lRest * Quaternion.Euler(-100f, 0f, 20f);
        Quaternion rUp = rRest * Quaternion.Euler(-100f, 0f, -20f);
        Quaternion hBack = hRest * Quaternion.Euler(-15f, 0f, 0f);
        Quaternion sArch = sRest * Quaternion.Euler(-8f, 0f, 0f);

        for (int i = 0; i < 3; i++)
        {
            // Crouch (prep)
            yield return MoveOverTime(Vector3.up, -0.04f, 0.04f);

            // Jump up while smoothly raising arms
            float upElapsed = 0f;
            Vector3 upStart = transform.localPosition;
            Vector3 upEnd = upStart + Vector3.up * (jumpHeight + 0.04f);
            while (upElapsed < upSpeed)
            {
                upElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, upElapsed / upSpeed);
                transform.localPosition = Vector3.Lerp(upStart, upEnd, t);
                if (leftArm != null) leftArm.localRotation = Quaternion.Slerp(lRest, lUp, t);
                if (rightArm != null) rightArm.localRotation = Quaternion.Slerp(rRest, rUp, t);
                if (head != null) head.localRotation = Quaternion.Slerp(hRest, hBack, t);
                if (spine != null) spine.localRotation = Quaternion.Slerp(sRest, sArch, t);
                yield return null;
            }

            // Fall down while smoothly returning bones
            float downElapsed = 0f;
            Vector3 downStart = transform.localPosition;
            Vector3 downEnd = downStart - Vector3.up * jumpHeight;
            while (downElapsed < downSpeed)
            {
                downElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, downElapsed / downSpeed);
                transform.localPosition = Vector3.Lerp(downStart, downEnd, t);
                if (leftArm != null) leftArm.localRotation = Quaternion.Slerp(lUp, lRest, t);
                if (rightArm != null) rightArm.localRotation = Quaternion.Slerp(rUp, rRest, t);
                if (head != null) head.localRotation = Quaternion.Slerp(hBack, hRest, t);
                if (spine != null) spine.localRotation = Quaternion.Slerp(sArch, sRest, t);
                yield return null;
            }
        }

        ResetState();
    }

    /// <summary> Head tilt to side using bone (thinking) </summary>
    IEnumerator DoTiltSide()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (head != null)
        {
            Quaternion rest = head.localRotation;
            float tiltAmt = VaryAngle(22f);
            float tiltDir = Random.value > 0.5f ? 1f : -1f;
            Quaternion tilted = rest * Quaternion.Euler(VaryAngle(5f), 0f, tiltAmt * tiltDir);
            yield return RotateBoneOverTime(head, rest, tilted, Vary(0.28f), EaseInOutBack);
            yield return new WaitForSeconds(Vary(0.75f));
            yield return RotateBoneOverTime(head, tilted, rest, Vary(0.28f), SmootherStep);
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

    /// <summary> Full-body wiggle — head, spine, arms all sway side-to-side </summary>
    IEnumerator DoWiggle()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftHand = poseManager?.GetBone(HumanBodyBones.LeftHand);
        Transform rightHand = poseManager?.GetBone(HumanBodyBones.RightHand);

        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion lhRest = leftHand != null ? leftHand.localRotation : Quaternion.identity;
        Quaternion rhRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;

        float amount = 0.05f;
        float speed = 0.06f;

        for (int i = 0; i < 6; i++)
        {
            float dir = (i % 2 == 0) ? 1f : -1f;
            // Translate side-to-side
            yield return MoveOverTime(Vector3.right, amount * dir, speed);
            // Spine lean + head tilt opposite + arms swing
            if (spine != null) spine.localRotation = sRest * Quaternion.Euler(0f, 0f, 12f * dir);
            if (head != null) head.localRotation = hRest * Quaternion.Euler(0f, 0f, -15f * dir);
            if (leftArm != null) leftArm.localRotation = lRest * Quaternion.Euler(-15f * dir, 0f, -10f * dir);
            if (rightArm != null) rightArm.localRotation = rRest * Quaternion.Euler(15f * dir, 0f, 10f * dir);
            if (leftHand != null) leftHand.localRotation = lhRest * Quaternion.Euler(0f, 0f, 15f * dir);
            if (rightHand != null) rightHand.localRotation = rhRest * Quaternion.Euler(0f, 0f, -15f * dir);
            yield return MoveOverTime(Vector3.right, -amount * dir, speed);
        }

        // Reset bones
        if (head != null) head.localRotation = hRest;
        if (spine != null) spine.localRotation = sRest;
        if (leftArm != null) leftArm.localRotation = lRest;
        if (rightArm != null) rightArm.localRotation = rRest;
        if (leftHand != null) leftHand.localRotation = lhRest;
        if (rightHand != null) rightHand.localRotation = rhRest;

        ResetState();
    }

    /// <summary> Quick squash and stretch (blink) </summary>
    IEnumerator DoSquash()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Vector3 squash = new Vector3(originalScale.x * 1.12f, originalScale.y * 0.78f, originalScale.z * 1.12f);

        yield return ScaleOverTime(squash, 0.1f);
        yield return ScaleOverTime(originalScale, 0.15f);

        ResetState();
    }

    /// <summary> Shoulders raise + arms out (shrug) — bone-based </summary>
    IEnumerator DoShrug()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
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
            Quaternion lOut  = lRest  * Quaternion.Euler(VaryAngle(-30f), 0f, VaryAngle(-15f));
            Quaternion rOut  = rRest  * Quaternion.Euler(VaryAngle(-30f), 0f, VaryAngle( 15f));
            Quaternion llBent = llRest * Quaternion.Euler(VaryAngle(-50f), 0f, 0f);
            Quaternion rlBent = rlRest * Quaternion.Euler(VaryAngle(-50f), 0f, 0f);
            Quaternion hTilt  = hRest  * Quaternion.Euler(0f, 0f, VaryAngle(10f));

            // Raise to shrug — all bones in parallel with EaseInOutBack weight
            float raiseDur = Vary(0.22f);
            StartCoroutine(RotateBoneOverTime(leftArm,  lRest,  lOut,   raiseDur,        EaseInOutBack));
            StartCoroutine(RotateBoneDelayed(rightArm,  rRest,  rOut,   raiseDur * 0.96f, 0.02f));
            if (leftLower  != null) StartCoroutine(RotateBoneDelayed(leftLower,  llRest, llBent, raiseDur * 0.85f, 0.05f));
            if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlRest, rlBent, raiseDur * 0.85f, 0.06f));
            if (head != null)       StartCoroutine(RotateBoneDelayed(head,       hRest,  hTilt,  raiseDur * 0.78f, 0.07f));
            yield return MoveOverTime(Vector3.up, 0.04f, raiseDur);

            yield return new WaitForSeconds(Vary(0.45f));

            // Lower back — all parallel
            float dropDur = Vary(0.24f);
            StartCoroutine(MoveOverTime(Vector3.up, -0.04f, dropDur));
            if (head       != null) StartCoroutine(RotateBoneOverTime(head,       hTilt,  hRest,  dropDur * 0.85f, SmootherStep));
            if (leftLower  != null) StartCoroutine(RotateBoneDelayed(leftLower,  llBent, llRest, dropDur * 0.82f, 0.03f));
            if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlBent, rlRest, dropDur * 0.82f, 0.04f));
            StartCoroutine(RotateBoneDelayed(rightArm, rOut, rRest, dropDur * 0.96f, 0.02f));
            yield return RotateBoneOverTime(leftArm, lOut, lRest, dropDur, SmootherStep);
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
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);

        if (head != null)
        {
            Quaternion rest = head.localRotation;
            for (int i = 0; i < 3; i++)
            {
                float amp = VaryAngle(22f);
                float spd = Vary(0.11f);
                Quaternion left  = rest * Quaternion.Euler(0f, -amp, 0f);
                Quaternion right = rest * Quaternion.Euler(0f,  amp, 0f);
                yield return RotateBoneOverTime(head, head.localRotation, left,  spd,        EaseInOutBack);
                yield return RotateBoneOverTime(head, left,  right, spd * 2f,   EaseInOutBack);
                yield return RotateBoneOverTime(head, right, rest,  spd,        SmootherStep);
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
        if (idleAnimator != null) idleAnimator.paused = true;
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

    /// <summary> Full-body dance groove — spine sway + arm pump + head bob + bounce </summary>
    IEnumerator DoDance()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform leftHand = poseManager?.GetBone(HumanBodyBones.LeftHand);
        Transform rightHand = poseManager?.GetBone(HumanBodyBones.RightHand);

        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
        Quaternion lhRest = leftHand != null ? leftHand.localRotation : Quaternion.identity;
        Quaternion rhRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;

        float beatTime = 0.22f;
        float bounceHeight = 0.10f;

        for (int i = 0; i < 8; i++)
        {
            float dir = (i % 2 == 0) ? 1f : -1f;

            // Target poses for this beat
            Quaternion sTarget = sRest * Quaternion.Euler(-5f, 8f * dir, 15f * dir);
            Quaternion hTarget = hRest * Quaternion.Euler(-8f, -5f * dir, 10f * dir);
            Quaternion lArmTarget, rArmTarget, llTarget, rlTarget;

            if (i % 2 == 0)
            {
                lArmTarget = lRest * Quaternion.Euler(-60f, 0f, 15f);
                llTarget = llRest * Quaternion.Euler(-70f, 0f, 0f);
                rArmTarget = rRest * Quaternion.Euler(-15f, 0f, -8f);
                rlTarget = rlRest * Quaternion.Euler(-20f, 0f, 0f);
            }
            else
            {
                rArmTarget = rRest * Quaternion.Euler(-60f, 0f, -15f);
                rlTarget = rlRest * Quaternion.Euler(-70f, 0f, 0f);
                lArmTarget = lRest * Quaternion.Euler(-15f, 0f, 8f);
                llTarget = llRest * Quaternion.Euler(-20f, 0f, 0f);
            }

            // Blend all bones to beat pose while bouncing up
            float upElapsed = 0f;
            Vector3 upStart = transform.localPosition;
            Vector3 upEnd = upStart + Vector3.up * bounceHeight;
            float halfBeat = beatTime * 0.5f;
            while (upElapsed < halfBeat)
            {
                upElapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, upElapsed / halfBeat);
                transform.localPosition = Vector3.Lerp(upStart, upEnd, t);
                if (spine != null) spine.localRotation = Quaternion.Slerp(spine.localRotation, sTarget, t * 0.5f + 0.5f);
                if (head != null) head.localRotation = Quaternion.Slerp(head.localRotation, hTarget, t * 0.5f + 0.5f);
                if (leftArm != null) leftArm.localRotation = Quaternion.Slerp(leftArm.localRotation, lArmTarget, t * 0.5f + 0.5f);
                if (rightArm != null) rightArm.localRotation = Quaternion.Slerp(rightArm.localRotation, rArmTarget, t * 0.5f + 0.5f);
                if (leftLower != null) leftLower.localRotation = Quaternion.Slerp(leftLower.localRotation, llTarget, t * 0.5f + 0.5f);
                if (rightLower != null) rightLower.localRotation = Quaternion.Slerp(rightLower.localRotation, rlTarget, t * 0.5f + 0.5f);
                yield return null;
            }

            // Bounce down
            yield return MoveOverTime(Vector3.up, -bounceHeight, halfBeat);
        }

        // Smooth return instead of snapping all bones at once
        // ResetState handles this smoothly now

        ResetState();
    }

    /// <summary> Shy peek — duck down with spine curl + arms cover face + peek up </summary>
    IEnumerator DoPeek()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;

        // Hide poses — spine curls, head ducks, arms come up to cover
        Quaternion sHide = sRest * Quaternion.Euler(25f, 0f, 0f);
        Quaternion hHide = hRest * Quaternion.Euler(30f, 0f, 0f);
        Quaternion lCover = lRest * Quaternion.Euler(-50f, 20f, 15f);
        Quaternion rCover = rRest * Quaternion.Euler(-50f, -20f, -15f);
        Quaternion llBent = llRest * Quaternion.Euler(-70f, 0f, 0f);
        Quaternion rlBent = rlRest * Quaternion.Euler(-70f, 0f, 0f);

        // Duck down + hide
        yield return MoveOverTime(Vector3.up, -0.15f, 0.2f);
        if (spine != null) yield return RotateBoneOverTime(spine, sRest, sHide, 0.2f);
        if (head != null) head.localRotation = hHide;
        if (leftArm != null) leftArm.localRotation = lCover;
        if (rightArm != null) rightArm.localRotation = rCover;
        if (leftLower != null) leftLower.localRotation = llBent;
        if (rightLower != null) rightLower.localRotation = rlBent;

        yield return new WaitForSeconds(0.4f);

        // Peek up — head lifts, spine uncurls partially, one arm lowers
        Quaternion hPeek = hRest * Quaternion.Euler(5f, 10f, 8f);
        Quaternion sPeek = sRest * Quaternion.Euler(10f, 0f, 0f);
        if (head != null) yield return RotateBoneOverTime(head, hHide, hPeek, 0.3f);
        if (spine != null) spine.localRotation = sPeek;
        if (rightArm != null) rightArm.localRotation = rRest * Quaternion.Euler(-25f, 0f, -8f);
        if (rightLower != null) rightLower.localRotation = rlRest * Quaternion.Euler(-30f, 0f, 0f);
        yield return MoveOverTime(Vector3.up, 0.10f, 0.3f);

        yield return new WaitForSeconds(0.5f);

        // Return to normal
        if (head != null) yield return RotateBoneOverTime(head, hPeek, hRest, 0.2f);
        if (spine != null) yield return RotateBoneOverTime(spine, sPeek, sRest, 0.2f);
        if (leftArm != null) leftArm.localRotation = lRest;
        if (rightArm != null) rightArm.localRotation = rRest;
        if (leftLower != null) leftLower.localRotation = llRest;
        if (rightLower != null) rightLower.localRotation = rlRest;
        yield return MoveOverTime(Vector3.up, 0.05f, 0.15f);

        ResetState();
    }

    /// <summary> Pout with head down + arms crossed-ish — bone-based </summary>
    IEnumerator DoPout()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
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
        if (idleAnimator != null) idleAnimator.paused = true;
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

            // Clap cycles — fast lerp so each open/close has momentum rather than a hard snap
            for (int i = 0; i < 5; i++)
            {
                float clapSpd = Vary(0.05f);
                // Open (arms apart)
                StartCoroutine(RotateBoneOverTime(leftArm,  leftArm.localRotation,  lOpen,    clapSpd, EaseOutBack));
                yield return    RotateBoneOverTime(rightArm, rightArm.localRotation, rOpen,    clapSpd, EaseOutBack);
                // Close (clap!)
                StartCoroutine(RotateBoneOverTime(leftArm,  lOpen,    lForward, clapSpd, EaseOutBack));
                yield return    RotateBoneOverTime(rightArm, rOpen,    rForward, clapSpd, EaseOutBack);
                // Tiny bounce impulse with each clap
                StartCoroutine(MoveOverTime(Vector3.up,  0.035f, clapSpd * 0.6f));
                yield return    MoveOverTime(Vector3.up, -0.035f, clapSpd * 0.6f);
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
        if (idleAnimator != null) idleAnimator.paused = true;
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

            // Snap! Ultra-fast lerp = perceptually instant but avoids frame-one pop
            float snapDur = 0.045f;
            StartCoroutine(RotateBoneOverTime(head,     hRest, hBack,   snapDur, EaseOutBack));
            if (spine    != null) StartCoroutine(RotateBoneOverTime(spine,    sRest, sBack,   snapDur, EaseOutBack));
            if (leftArm  != null) StartCoroutine(RotateBoneOverTime(leftArm,  lRest, lFlinch, snapDur, EaseOutBack));
            if (rightArm != null) StartCoroutine(RotateBoneOverTime(rightArm, rRest, rFlinch, snapDur, EaseOutBack));

            // Body jolts up
            yield return MoveOverTime(Vector3.up, 0.08f, 0.06f);
            yield return new WaitForSeconds(Vary(0.45f));

            // Settle back — head + spine + arms all return simultaneously
            float settleDur = Vary(0.3f);
            StartCoroutine(MoveOverTime(Vector3.up, -0.08f, settleDur * 1.1f));
            StartCoroutine(RotateBoneOverTime(head,    hBack,   hRest,  settleDur, SmootherStep));
            if (spine    != null) StartCoroutine(RotateBoneDelayed(spine,    sBack,   sRest,  settleDur * 0.88f, 0.03f));
            if (leftArm  != null) StartCoroutine(RotateBoneDelayed(leftArm,  lFlinch, lRest,  settleDur * 0.82f, 0.05f));
            if (rightArm != null) StartCoroutine(RotateBoneDelayed(rightArm, rFlinch, rRest,  settleDur * 0.82f, 0.06f));
            yield return new WaitForSeconds(settleDur);
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
        if (idleAnimator != null) idleAnimator.paused = true;
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
        if (idleAnimator != null) idleAnimator.paused = true;
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
        if (idleAnimator != null) idleAnimator.paused = true;
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
        if (idleAnimator != null) idleAnimator.paused = true;
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
        if (idleAnimator != null) idleAnimator.paused = true;
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
        if (idleAnimator != null) idleAnimator.paused = true;
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

            // Cross — all arms in parallel, EaseInOutBack for that weighted feel
            float crossDur = Vary(0.32f);
            StartCoroutine(RotateBoneOverTime(leftArm,  lRest,  lCross, crossDur,         EaseInOutBack));
            StartCoroutine(RotateBoneDelayed(rightArm,  rRest,  rCross, crossDur * 0.92f, 0.03f));
            if (leftLower  != null) StartCoroutine(RotateBoneDelayed(leftLower,  llRest, llBent, crossDur * 0.78f, 0.07f));
            if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlRest, rlBent, crossDur * 0.78f, 0.09f));
            if (head != null)       StartCoroutine(RotateBoneDelayed(head,       hRest,  hTilt,  crossDur * 0.65f, 0.12f));
            yield return new WaitForSeconds(crossDur);

            yield return new WaitForSeconds(Vary(0.75f));

            // Uncross — all parallel
            float uncrossDur = Vary(0.3f);
            if (head       != null) StartCoroutine(RotateBoneOverTime(head,       hTilt,  hRest,  uncrossDur * 0.7f,  SmootherStep));
            if (leftLower  != null) StartCoroutine(RotateBoneDelayed(leftLower,  llBent, llRest, uncrossDur * 0.75f, 0.04f));
            if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlBent, rlRest, uncrossDur * 0.75f, 0.05f));
            StartCoroutine(RotateBoneDelayed(rightArm, rCross, rRest, uncrossDur * 0.9f, 0.02f));
            yield return RotateBoneOverTime(leftArm, lCross, lRest, uncrossDur, SmootherStep);
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
        if (idleAnimator != null) idleAnimator.paused = true;
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

            // Both arms shoot up simultaneously with EaseOutBack — feels like a celebration burst
            float upDur = Vary(0.28f);
            StartCoroutine(RotateBoneOverTime(leftArm,  lRest,  lUp,   upDur,         EaseOutBack));
            StartCoroutine(RotateBoneDelayed(rightArm,  rRest,  rUp,   upDur * 0.96f, 0.015f));
            if (leftLower  != null) StartCoroutine(RotateBoneDelayed(leftLower,  llRest, llStr, upDur * 0.82f, 0.05f));
            if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlRest, rlStr, upDur * 0.82f, 0.06f));
            if (spine != null)      StartCoroutine(RotateBoneDelayed(spine,      sRest,  sArch, upDur * 0.7f,  0.08f));
            yield return new WaitForSeconds(upDur);

            // Victory bounces
            for (int i = 0; i < 3; i++)
            {
                yield return MoveOverTime(Vector3.up,  0.08f, Vary(0.08f));
                yield return MoveOverTime(Vector3.up, -0.08f, Vary(0.07f));
            }

            // Lower — all parallel, SmootherStep for a gentle float down
            float downDur = Vary(0.32f);
            if (spine      != null) StartCoroutine(RotateBoneOverTime(spine,     sArch,  sRest,  downDur * 0.78f, SmootherStep));
            if (leftLower  != null) StartCoroutine(RotateBoneDelayed(leftLower,  llStr,  llRest, downDur * 0.82f, 0.03f));
            if (rightLower != null) StartCoroutine(RotateBoneDelayed(rightLower, rlStr,  rlRest, downDur * 0.82f, 0.04f));
            StartCoroutine(RotateBoneDelayed(rightArm, rUp, rRest, downDur * 0.95f, 0.015f));
            yield return RotateBoneOverTime(leftArm, lUp, lRest, downDur, SmootherStep);
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

    /// <summary> Finger-to-chin thinking pose — arm lifts to chin height, head micro-tilts while pondering </summary>
    IEnumerator DoThinking()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform rightArm   = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform rightHand  = poseManager?.GetBone(HumanBodyBones.RightHand);
        Transform head  = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        Quaternion armRest   = rightArm   != null ? rightArm.localRotation   : Quaternion.identity;
        Quaternion lowerRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
        Quaternion handRest  = rightHand  != null ? rightHand.localRotation  : Quaternion.identity;
        Quaternion headRest  = head  != null ? head.localRotation  : Quaternion.identity;
        Quaternion spineRest = spine != null ? spine.localRotation : Quaternion.identity;

        if (rightArm != null)
        {
            // Arm rises to chin height, elbow bent inward, slight wrist rotation
            Quaternion armChin   = armRest   * Quaternion.Euler(-65f,  20f, -35f);
            Quaternion lowerBent = lowerRest * Quaternion.Euler(-85f,   0f,   0f);
            Quaternion handAngle = handRest  * Quaternion.Euler(  0f,   0f,  20f);
            // Head tilts and looks slightly up while pondering; spine leans back minimally
            Quaternion headPonder = headRest  * Quaternion.Euler( 5f, 12f, -8f);
            Quaternion spineBack  = spineRest * Quaternion.Euler(-4f,  0f,  4f);

            if (spine != null) spine.localRotation = spineBack;
            yield return RotateBoneOverTime(rightArm, armRest, armChin, 0.3f);
            if (rightLower != null) rightLower.localRotation = lowerBent;
            if (rightHand  != null) rightHand.localRotation  = handAngle;
            if (head != null) yield return RotateBoneOverTime(head, headRest, headPonder, 0.2f);

            // Hold — alternating micro-tilts make the thinking look alive
            Quaternion headAlt = headRest * Quaternion.Euler(3f, -10f, 6f);
            yield return new WaitForSeconds(0.35f);
            yield return RotateBoneOverTime(head, headPonder, headAlt, 0.35f);
            yield return new WaitForSeconds(0.3f);
            yield return RotateBoneOverTime(head, headAlt, headPonder, 0.3f);
            yield return new WaitForSeconds(0.2f);

            // Lower everything back
            if (head != null) yield return RotateBoneOverTime(head, head.localRotation, headRest, 0.2f);
            if (rightLower != null) rightLower.localRotation = lowerRest;
            if (rightHand  != null) rightHand.localRotation  = handRest;
            if (spine != null) spine.localRotation = spineRest;
            yield return RotateBoneOverTime(rightArm, armChin, armRest, 0.3f);
        }
        else
        {
            yield return RotateOverTime(Vector3.right, -10f, 0.3f);
            yield return new WaitForSeconds(0.9f);
            yield return RotateOverTime(Vector3.right, 10f, 0.3f);
        }

        ResetState();
    }

    /// <summary> Startled surprise — both arms snap up instantly, body steps back, then smooth recovery </summary>
    IEnumerator DoSurprise()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform leftArm    = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm   = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower  = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform head  = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        Quaternion lRest  = leftArm   != null ? leftArm.localRotation   : Quaternion.identity;
        Quaternion rRest  = rightArm  != null ? rightArm.localRotation  : Quaternion.identity;
        Quaternion llRest = leftLower  != null ? leftLower.localRotation  : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
        Quaternion hRest  = head  != null ? head.localRotation  : Quaternion.identity;
        Quaternion sRest  = spine != null ? spine.localRotation : Quaternion.identity;

        // Arms fly upward, elbows bent outward — startled "hands up" pose
        Quaternion lUp   = lRest  * Quaternion.Euler(-85f,  0f,  25f);
        Quaternion rUp   = rRest  * Quaternion.Euler(-85f,  0f, -25f);
        Quaternion llOut = llRest * Quaternion.Euler(-50f,  0f,   0f);
        Quaternion rlOut = rlRest * Quaternion.Euler(-50f,  0f,   0f);
        Quaternion hBack = hRest  * Quaternion.Euler(-22f,  0f,   0f);
        Quaternion sBack = sRest  * Quaternion.Euler( -6f,  0f,   0f);

        // Snap! Everything moves at once (surprise is instant)
        if (leftArm    != null) leftArm.localRotation    = lUp;
        if (rightArm   != null) rightArm.localRotation   = rUp;
        if (leftLower  != null) leftLower.localRotation  = llOut;
        if (rightLower != null) rightLower.localRotation = rlOut;
        if (head  != null) head.localRotation  = hBack;
        if (spine != null) spine.localRotation = sBack;

        // Quick step backward on same frame as jolt
        yield return MoveOverTime(Vector3.back, 0.06f, 0.07f);

        // Hold surprised pose
        yield return new WaitForSeconds(0.45f);

        // Smooth simultaneous recovery — blend all bones back together
        float recoverTime = 0.45f;
        float recElapsed  = 0f;
        Vector3 backPos = transform.localPosition;
        while (recElapsed < recoverTime)
        {
            recElapsed += Time.deltaTime;
            float t = SmootherStep(recElapsed / recoverTime);
            if (leftArm    != null) leftArm.localRotation    = Quaternion.Slerp(lUp,   lRest,  t);
            if (rightArm   != null) rightArm.localRotation   = Quaternion.Slerp(rUp,   rRest,  t);
            if (leftLower  != null) leftLower.localRotation  = Quaternion.Slerp(llOut, llRest, t);
            if (rightLower != null) rightLower.localRotation = Quaternion.Slerp(rlOut, rlRest, t);
            if (head  != null) head.localRotation  = Quaternion.Slerp(hBack, hRest, t);
            if (spine != null) spine.localRotation = Quaternion.Slerp(sBack, sRest, t);
            transform.localPosition = Vector3.Lerp(backPos, originalPos, t);
            yield return null;
        }

        // Snap to exact rest
        if (leftArm    != null) leftArm.localRotation    = lRest;
        if (rightArm   != null) rightArm.localRotation   = rRest;
        if (leftLower  != null) leftLower.localRotation  = llRest;
        if (rightLower != null) rightLower.localRotation = rlRest;
        if (head  != null) head.localRotation  = hRest;
        if (spine != null) spine.localRotation = sRest;
        transform.localPosition = originalPos;

        ResetState();
    }

    /// <summary> Happy hop — anticipation crouch, single bright hop with arms wide, landing squish </summary>
    IEnumerator DoHappy()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform leftArm    = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm   = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower  = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform head  = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        Quaternion lRest  = leftArm   != null ? leftArm.localRotation   : Quaternion.identity;
        Quaternion rRest  = rightArm  != null ? rightArm.localRotation  : Quaternion.identity;
        Quaternion llRest = leftLower  != null ? leftLower.localRotation  : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
        Quaternion hRest  = head  != null ? head.localRotation  : Quaternion.identity;
        Quaternion sRest  = spine != null ? spine.localRotation : Quaternion.identity;

        // Peak-of-hop pose: arms wide and up, spine arched back, head tilts back
        Quaternion lUp    = lRest  * Quaternion.Euler(-95f,  0f,  22f);
        Quaternion rUp    = rRest  * Quaternion.Euler(-95f,  0f, -22f);
        Quaternion llBent = llRest * Quaternion.Euler(-30f,  0f,   0f);
        Quaternion rlBent = rlRest * Quaternion.Euler(-30f,  0f,   0f);
        Quaternion hBack  = hRest  * Quaternion.Euler(-12f,  0f,   0f);
        Quaternion sArch  = sRest  * Quaternion.Euler( -6f,  0f,   0f);

        // Anticipation: small crouch before hop
        yield return MoveOverTime(Vector3.up, -0.03f, 0.06f);

        // Hop up — blend bones to peak pose simultaneously
        float hopUp = 0.12f;
        float hopElapsed = 0f;
        Vector3 hopStart = transform.localPosition;
        Vector3 hopPeak  = hopStart + Vector3.up * 0.2f;
        while (hopElapsed < hopUp)
        {
            hopElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, hopElapsed / hopUp);
            transform.localPosition = Vector3.Lerp(hopStart, hopPeak, t);
            if (leftArm    != null) leftArm.localRotation    = Quaternion.Slerp(lRest,  lUp,    t);
            if (rightArm   != null) rightArm.localRotation   = Quaternion.Slerp(rRest,  rUp,    t);
            if (leftLower  != null) leftLower.localRotation  = Quaternion.Slerp(llRest, llBent, t);
            if (rightLower != null) rightLower.localRotation = Quaternion.Slerp(rlRest, rlBent, t);
            if (head  != null) head.localRotation  = Quaternion.Slerp(hRest, hBack, t);
            if (spine != null) spine.localRotation = Quaternion.Slerp(sRest, sArch, t);
            yield return null;
        }

        // Land — blend bones back to rest simultaneously
        float hopDown = 0.10f;
        float landElapsed = 0f;
        Vector3 landStart = transform.localPosition;
        Vector3 landEnd   = hopStart;
        while (landElapsed < hopDown)
        {
            landElapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, landElapsed / hopDown);
            transform.localPosition = Vector3.Lerp(landStart, landEnd, t);
            if (leftArm    != null) leftArm.localRotation    = Quaternion.Slerp(lUp,    lRest,  t);
            if (rightArm   != null) rightArm.localRotation   = Quaternion.Slerp(rUp,    rRest,  t);
            if (leftLower  != null) leftLower.localRotation  = Quaternion.Slerp(llBent, llRest, t);
            if (rightLower != null) rightLower.localRotation = Quaternion.Slerp(rlBent, rlRest, t);
            if (head  != null) head.localRotation  = Quaternion.Slerp(hBack, hRest, t);
            if (spine != null) spine.localRotation = Quaternion.Slerp(sArch, sRest, t);
            yield return null;
        }

        // Landing squish
        yield return MoveOverTime(Vector3.up, -0.025f, 0.05f);
        yield return MoveOverTime(Vector3.up,  0.025f, 0.08f);

        // Settle
        if (leftArm    != null) leftArm.localRotation    = lRest;
        if (rightArm   != null) rightArm.localRotation   = rRest;
        if (leftLower  != null) leftLower.localRotation  = llRest;
        if (rightLower != null) rightLower.localRotation = rlRest;
        if (head  != null) head.localRotation  = hRest;
        if (spine != null) spine.localRotation = sRest;

        ResetState();
    }

    // --- Helper coroutines ---

    /// <summary> Perlin's SmootherStep — zero velocity AND zero acceleration at both ends. </summary>
    private static float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// <summary>
    /// EaseOutBack — accelerates fast then overshoots the target by ~17% and settles back.
    /// Use for snappy "thrown" movements like raising an arm or a head snap.
    /// </summary>
    private static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>
    /// EaseInOutBack — small anticipation wind-up at the start, slight overshoot at the end.
    /// Use for deliberate gestures that feel weighted (e.g. shrug, clap).
    /// </summary>
    private static float EaseInOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.70158f;
        const float c2 = c1 * 1.525f;
        return t < 0.5f
            ? (Mathf.Pow(2f * t, 2f) * ((c2 + 1f) * 2f * t - c2)) / 2f
            : (Mathf.Pow(2f * t - 2f, 2f) * ((c2 + 1f) * (t * 2f - 2f) + c2) + 2f) / 2f;
    }

    /// <summary>
    /// EaseOutElastic — springy bounce that decays to rest. Slightly overshoots then rings.
    /// Use for repetitive oscillating motion like a hand wave or a finger wag.
    /// </summary>
    private static float EaseOutElastic(float t)
    {
        t = Mathf.Clamp01(t);
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        const float c4 = (2f * Mathf.PI) / 3f;
        return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
    }

    /// <summary>
    /// Variant of RotateBoneOverTime that accepts a custom easing function.
    /// Pass SmootherStep, EaseOutBack, EaseOutElastic, EaseInOutBack, etc.
    /// </summary>
    IEnumerator RotateBoneOverTime(Transform bone, Quaternion from, Quaternion to, float duration,
                                    System.Func<float, float> ease)
    {
        if (bone == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ease(elapsed / duration);
            bone.localRotation = Quaternion.Slerp(from, to, t);
            yield return null;
        }
        bone.localRotation = to;
    }

    /// <summary> Start a bone rotation after a delay — enables cascading parallel movement. </summary>
    IEnumerator RotateBoneDelayed(Transform bone, Quaternion from, Quaternion to, float duration, float delay)
    {
        if (bone == null) yield break;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        yield return RotateBoneOverTime(bone, from, to, duration);
    }

    IEnumerator RotateOverTime(Vector3 axis, float angle, float duration)
    {
        float elapsed = 0f;
        Quaternion startRot = transform.localRotation;
        Quaternion endRot = transform.localRotation * Quaternion.AngleAxis(angle, axis);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = SmootherStep(elapsed / duration);
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
            float t = SmootherStep(elapsed / duration);
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
            float t = SmootherStep(elapsed / duration);
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
            float t = SmootherStep(elapsed / duration);
            bone.localRotation = Quaternion.Slerp(from, to, t);
            yield return null;
        }
        bone.localRotation = to;
    }

    private void ResetState()
    {
        // Use smooth reset — starts a coroutine that blends everything back
        StartCoroutine(SmoothResetState(0.5f));
    }

    /// <summary>
    /// Smoothly blend transform + bones back to rest over the given duration.
    /// Much more fluid than snapping everything in one frame.
    /// </summary>
    private IEnumerator SmoothResetState(float duration)
    {
        // Snapshot current transform values
        Vector3 startPos = transform.localPosition;
        Quaternion startRot = transform.localRotation;
        Vector3 startScale = transform.localScale;

        // Start smooth bone reset in parallel
        Coroutine boneReset = null;
        if (poseManager != null)
            boneReset = StartCoroutine(poseManager.SmoothResetToRestPose(duration));

        // Blend transform back
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = SmootherStep(elapsed / duration);
            transform.localPosition = Vector3.Lerp(startPos, originalPos, t);
            transform.localRotation = Quaternion.Slerp(startRot, originalRot, t);
            transform.localScale = Vector3.Lerp(startScale, originalScale, t);
            yield return null;
        }

        // Ensure exact final values
        transform.localPosition = originalPos;
        transform.localRotation = originalRot;
        transform.localScale = originalScale;

        // Resume idle bone animations (blend weight handles smooth transition)
        if (idleAnimator != null)
            idleAnimator.paused = false;

        isPlaying = false;
        Debug.Log("EmoteAnimator: Full emote finished (smooth), isPlaying = false");
    }

    // --- Additional Dramatic Animations ---

    /// <summary> Deep bend over — dramatic forward lean with arms dangling </summary>
    IEnumerator DoBendOver()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform chest = poseManager?.GetBone(HumanBodyBones.Chest);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion cRest = chest != null ? chest.localRotation : Quaternion.identity;
        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;

        // Deep forward lean
        Quaternion sBend = sRest * Quaternion.Euler(35f, 0f, 0f);
        Quaternion cBend = cRest * Quaternion.Euler(20f, 0f, 0f);
        Quaternion hDrop = hRest * Quaternion.Euler(15f, 0f, 0f);
        // Arms dangle forward
        Quaternion lDangle = lRest * Quaternion.Euler(25f, 0f, 5f);
        Quaternion rDangle = rRest * Quaternion.Euler(25f, 0f, -5f);
        Quaternion llLoose = llRest * Quaternion.Euler(10f, 0f, 0f);
        Quaternion rlLoose = rlRest * Quaternion.Euler(10f, 0f, 0f);

        // Bend down
        yield return RotateBoneOverTime(spine, sRest, sBend, 0.35f);
        if (chest != null) yield return RotateBoneOverTime(chest, cRest, cBend, 0.2f);
        if (head != null) head.localRotation = hDrop;
        if (leftArm != null) leftArm.localRotation = lDangle;
        if (rightArm != null) rightArm.localRotation = rDangle;
        if (leftLower != null) leftLower.localRotation = llLoose;
        if (rightLower != null) rightLower.localRotation = rlLoose;

        // Slight sway while bent
        for (int i = 0; i < 2; i++)
        {
            if (head != null) head.localRotation = hDrop * Quaternion.Euler(0f, 8f, 0f);
            yield return new WaitForSeconds(0.25f);
            if (head != null) head.localRotation = hDrop * Quaternion.Euler(0f, -8f, 0f);
            yield return new WaitForSeconds(0.25f);
        }

        // Stand back up
        if (head != null) yield return RotateBoneOverTime(head, head.localRotation, hRest, 0.2f);
        if (chest != null) yield return RotateBoneOverTime(chest, cBend, cRest, 0.2f);
        yield return RotateBoneOverTime(spine, sBend, sRest, 0.35f);
        if (leftArm != null) leftArm.localRotation = lRest;
        if (rightArm != null) rightArm.localRotation = rRest;
        if (leftLower != null) leftLower.localRotation = llRest;
        if (rightLower != null) rightLower.localRotation = rlRest;

        ResetState();
    }

    /// <summary> Both arms waving — big dramatic greeting/goodbye </summary>
    IEnumerator DoBigWave()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);
        Transform leftHand = poseManager?.GetBone(HumanBodyBones.LeftHand);
        Transform rightHand = poseManager?.GetBone(HumanBodyBones.RightHand);
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);

        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
        Quaternion lhRest = leftHand != null ? leftHand.localRotation : Quaternion.identity;
        Quaternion rhRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;
        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;

        // Both arms way up
        Quaternion lUp = lRest * Quaternion.Euler(-120f, 0f, 25f);
        Quaternion rUp = rRest * Quaternion.Euler(-120f, 0f, -25f);
        Quaternion llBent = llRest * Quaternion.Euler(-30f, 0f, 0f);
        Quaternion rlBent = rlRest * Quaternion.Euler(-30f, 0f, 0f);

        // Raise arms
        yield return RotateBoneOverTime(leftArm, lRest, lUp, 0.2f);
        if (rightArm != null) rightArm.localRotation = rUp;
        if (leftLower != null) leftLower.localRotation = llBent;
        if (rightLower != null) rightLower.localRotation = rlBent;

        // Wave both arms with body sway
        for (int i = 0; i < 5; i++)
        {
            float dir = (i % 2 == 0) ? 1f : -1f;
            if (leftArm != null) leftArm.localRotation = lUp * Quaternion.Euler(0f, 0f, 25f * dir);
            if (rightArm != null) rightArm.localRotation = rUp * Quaternion.Euler(0f, 0f, 25f * dir);
            if (leftHand != null) leftHand.localRotation = lhRest * Quaternion.Euler(0f, 0f, 30f * dir);
            if (rightHand != null) rightHand.localRotation = rhRest * Quaternion.Euler(0f, 0f, -30f * dir);
            if (spine != null) spine.localRotation = sRest * Quaternion.Euler(0f, 0f, 6f * dir);
            if (head != null) head.localRotation = hRest * Quaternion.Euler(0f, 0f, -8f * dir);
            yield return new WaitForSeconds(0.15f);
        }

        // Lower
        if (head != null) head.localRotation = hRest;
        if (spine != null) spine.localRotation = sRest;
        if (leftHand != null) leftHand.localRotation = lhRest;
        if (rightHand != null) rightHand.localRotation = rhRest;
        if (leftLower != null) leftLower.localRotation = llRest;
        if (rightLower != null) rightLower.localRotation = rlRest;
        yield return RotateBoneOverTime(leftArm, leftArm.localRotation, lRest, 0.25f);
        if (rightArm != null) rightArm.localRotation = rRest;

        ResetState();
    }

    /// <summary> Stumble/recoil back — full body jolt backward </summary>
    IEnumerator DoRecoil()
    {
        isPlaying = true;
        if (idleAnimator != null) idleAnimator.paused = true;
        Transform head = poseManager?.GetBone(HumanBodyBones.Head);
        Transform spine = poseManager?.GetBone(HumanBodyBones.Spine);
        Transform chest = poseManager?.GetBone(HumanBodyBones.Chest);
        Transform leftArm = poseManager?.GetBone(HumanBodyBones.LeftUpperArm);
        Transform rightArm = poseManager?.GetBone(HumanBodyBones.RightUpperArm);
        Transform leftLower = poseManager?.GetBone(HumanBodyBones.LeftLowerArm);
        Transform rightLower = poseManager?.GetBone(HumanBodyBones.RightLowerArm);

        Quaternion hRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion sRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion cRest = chest != null ? chest.localRotation : Quaternion.identity;
        Quaternion lRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion llRest = leftLower != null ? leftLower.localRotation : Quaternion.identity;
        Quaternion rlRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;

        // Full body jolts back — head, spine, chest arch backward, arms fling out
        Quaternion hJolt = hRest * Quaternion.Euler(-25f, 0f, 5f);
        Quaternion sJolt = sRest * Quaternion.Euler(-18f, 0f, 0f);
        Quaternion cJolt = cRest * Quaternion.Euler(-12f, 0f, 0f);
        Quaternion lFling = lRest * Quaternion.Euler(-40f, 0f, -35f);
        Quaternion rFling = rRest * Quaternion.Euler(-40f, 0f, 35f);
        Quaternion llOut = llRest * Quaternion.Euler(-30f, 0f, 0f);
        Quaternion rlOut = rlRest * Quaternion.Euler(-30f, 0f, 0f);

        // Snap jolt (fast!)
        if (head != null) head.localRotation = hJolt;
        if (spine != null) spine.localRotation = sJolt;
        if (chest != null) chest.localRotation = cJolt;
        if (leftArm != null) leftArm.localRotation = lFling;
        if (rightArm != null) rightArm.localRotation = rFling;
        if (leftLower != null) leftLower.localRotation = llOut;
        if (rightLower != null) rightLower.localRotation = rlOut;
        yield return MoveOverTime(Vector3.up, 0.10f, 0.06f);

        yield return new WaitForSeconds(0.4f);

        // Recover slowly
        yield return MoveOverTime(Vector3.up, -0.10f, 0.3f);
        if (head != null) yield return RotateBoneOverTime(head, hJolt, hRest, 0.3f);
        if (spine != null) yield return RotateBoneOverTime(spine, sJolt, sRest, 0.25f);
        if (chest != null) chest.localRotation = cRest;
        if (leftArm != null) yield return RotateBoneOverTime(leftArm, lFling, lRest, 0.2f);
        if (rightArm != null) rightArm.localRotation = rRest;
        if (leftLower != null) leftLower.localRotation = llRest;
        if (rightLower != null) rightLower.localRotation = rlRest;

        ResetState();
    }
}
