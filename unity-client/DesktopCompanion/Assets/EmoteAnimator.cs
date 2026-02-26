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
    private IdleAnimator idleAnimator;

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

    /// <summary>
    /// Trigger an emote animation by keyword.
    /// Accepts any text — uses aggressive fuzzy matching to find the best animation.
    /// </summary>
    public void PlayEmote(string emote)
    {
        if (isPlaying) return; // Don't stack animations

        string lower = emote.ToLower().Trim();

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
            StartCoroutine(DoTiltSide());
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
        else if (lower.Contains("gasp") || lower.Contains("shock") || lower.Contains("surprise")
            || lower.Contains("omg") || lower.Contains("whoa") || lower.Contains("wow")
            || lower.Contains("really") || lower.Contains("what!"))
            StartCoroutine(DoGasp());
        else if (lower.Contains("pout") || lower.Contains("sulk") || lower.Contains("grumpy")
            || lower.Contains("hmph") || lower.Contains("angry") || lower.Contains("annoy")
            || lower.Contains("frustrat") || lower.Contains("mad"))
            StartCoroutine(DoPout());
        else if (lower.Contains("jump") || lower.Contains("excited") || lower.Contains("happy")
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

        switch (lower)
        {
            case "joy":
                pick = Random.Range(0, 7);
                switch (pick)
                {
                    case 0: StartCoroutine(DoBounce()); break;
                    case 1: StartCoroutine(DoJump()); break;
                    case 2: StartCoroutine(DoClap()); break;
                    case 3: StartCoroutine(DoHandsUp()); break;
                    case 4: StartCoroutine(DoDance()); break;
                    case 5: StartCoroutine(DoBigWave()); break;
                    case 6: StartCoroutine(DoWave()); break;
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
                pick = Random.Range(0, 6);
                switch (pick)
                {
                    case 0: StartCoroutine(DoDeflate()); break;
                    case 1: StartCoroutine(DoLookAway()); break;
                    case 2: StartCoroutine(DoGentle()); break;
                    case 3: StartCoroutine(DoShrug()); break;
                    case 4: StartCoroutine(DoBendOver()); break;
                    case 5: StartCoroutine(DoPeek()); break;
                }
                break;
            case "fun":
                pick = Random.Range(0, 7);
                switch (pick)
                {
                    case 0: StartCoroutine(DoWiggle()); break;
                    case 1: StartCoroutine(DoSpin()); break;
                    case 2: StartCoroutine(DoPeek()); break;
                    case 3: StartCoroutine(DoWave()); break;
                    case 4: StartCoroutine(DoDance()); break;
                    case 5: StartCoroutine(DoBigWave()); break;
                    case 6: StartCoroutine(DoBounce()); break;
                }
                break;
            default: // neutral
                pick = Random.Range(0, 7);
                switch (pick)
                {
                    case 0: StartCoroutine(DoNod()); break;
                    case 1: StartCoroutine(DoTiltSide()); break;
                    case 2: StartCoroutine(DoChinRest()); break;
                    case 3: StartCoroutine(DoPoint()); break;
                    case 4: StartCoroutine(DoShrug()); break;
                    case 5: StartCoroutine(DoBendOver()); break;
                    case 6: StartCoroutine(DoWave()); break;
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

            // Lean forward slightly while nodding + small hand gesture
            Quaternion sLean = sRest * Quaternion.Euler(8f, 0f, 0f);
            Quaternion aGesture = aRest * Quaternion.Euler(-20f, 0f, -12f);
            Quaternion lBend = lRest * Quaternion.Euler(-30f, 0f, 0f);

            if (spine != null) spine.localRotation = sLean;
            if (rightArm != null) rightArm.localRotation = aGesture;
            if (rightLower != null) rightLower.localRotation = lBend;

            for (int i = 0; i < 3; i++)
            {
                Quaternion down = hRest * Quaternion.Euler(25f, 0f, 0f);
                yield return RotateBoneOverTime(head, head.localRotation, down, 0.12f);
                yield return RotateBoneOverTime(head, down, hRest, 0.12f);
            }

            // Return all
            if (spine != null) yield return RotateBoneOverTime(spine, sLean, sRest, 0.2f);
            if (rightArm != null) yield return RotateBoneOverTime(rightArm, aGesture, aRest, 0.15f);
            if (rightLower != null) rightLower.localRotation = lRest;
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
            Quaternion armRest = rightArm.localRotation;
            Quaternion lowerRest = rightLower != null ? rightLower.localRotation : Quaternion.identity;
            Quaternion handRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;
            Quaternion headRest = head != null ? head.localRotation : Quaternion.identity;
            Quaternion spineRest = spine != null ? spine.localRotation : Quaternion.identity;

            // Big arm raise + elbow bend + wrist wave + head tilt + body lean
            Quaternion armRaised = armRest * Quaternion.Euler(-110f, 0f, -50f);
            Quaternion lowerBent = lowerRest * Quaternion.Euler(-40f, 0f, 0f);
            Quaternion headTilt = headRest * Quaternion.Euler(0f, 0f, -10f);
            Quaternion spineLean = spineRest * Quaternion.Euler(0f, 0f, -6f);

            // Raise everything
            yield return RotateBoneOverTime(rightArm, armRest, armRaised, 0.2f);
            if (rightLower != null) rightLower.localRotation = lowerBent;
            if (head != null) head.localRotation = headTilt;
            if (spine != null) spine.localRotation = spineLean;

            // Wave back and forth with HAND rotation for visible wrist movement
            for (int i = 0; i < 4; i++)
            {
                Quaternion waveA = armRaised * Quaternion.Euler(0f, 0f, 30f);
                Quaternion waveB = armRaised * Quaternion.Euler(0f, 0f, -30f);
                Quaternion handA = handRest * Quaternion.Euler(0f, 0f, 35f);
                Quaternion handB = handRest * Quaternion.Euler(0f, 0f, -35f);
                yield return RotateBoneOverTime(rightArm, rightArm.localRotation, waveA, 0.1f);
                if (rightHand != null) rightHand.localRotation = handA;
                yield return RotateBoneOverTime(rightArm, waveA, waveB, 0.2f);
                if (rightHand != null) rightHand.localRotation = handB;
                yield return RotateBoneOverTime(rightArm, waveB, armRaised, 0.1f);
                if (rightHand != null) rightHand.localRotation = handRest;
            }

            // Lower everything back
            if (head != null) yield return RotateBoneOverTime(head, headTilt, headRest, 0.15f);
            if (spine != null) spine.localRotation = spineRest;
            if (rightLower != null) rightLower.localRotation = lowerRest;
            if (rightHand != null) rightHand.localRotation = handRest;
            yield return RotateBoneOverTime(rightArm, rightArm.localRotation, armRest, 0.25f);
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
        float speed = 0.08f;

        Quaternion headRest = head != null ? head.localRotation : Quaternion.identity;
        Quaternion spineRest = spine != null ? spine.localRotation : Quaternion.identity;
        Quaternion lArmRest = leftArm != null ? leftArm.localRotation : Quaternion.identity;
        Quaternion rArmRest = rightArm != null ? rightArm.localRotation : Quaternion.identity;
        Quaternion lHandRest = leftHand != null ? leftHand.localRotation : Quaternion.identity;
        Quaternion rHandRest = rightHand != null ? rightHand.localRotation : Quaternion.identity;

        for (int i = 0; i < 5; i++)
        {
            // Bounce body up
            yield return MoveOverTime(Vector3.up, bounceHeight, speed);
            // Head, spine, arms all move at peak
            if (head != null)
                head.localRotation = headRest * Quaternion.Euler(-15f, (i % 2 == 0 ? 10f : -10f), (i % 2 == 0 ? 8f : -8f));
            if (spine != null)
                spine.localRotation = spineRest * Quaternion.Euler(-6f, 0f, (i % 2 == 0 ? 8f : -8f));
            // Arms flap out on each bounce
            if (leftArm != null)
                leftArm.localRotation = lArmRest * Quaternion.Euler(-25f, 0f, -20f);
            if (rightArm != null)
                rightArm.localRotation = rArmRest * Quaternion.Euler(-25f, 0f, 20f);
            if (leftHand != null)
                leftHand.localRotation = lHandRest * Quaternion.Euler(0f, 0f, 20f);
            if (rightHand != null)
                rightHand.localRotation = rHandRest * Quaternion.Euler(0f, 0f, -20f);

            // Bounce back down
            yield return MoveOverTime(Vector3.up, -bounceHeight, speed);
            // Return arms in
            if (head != null) head.localRotation = headRest;
            if (spine != null) spine.localRotation = spineRest;
            if (leftArm != null) leftArm.localRotation = lArmRest;
            if (rightArm != null) rightArm.localRotation = rArmRest;
            if (leftHand != null) leftHand.localRotation = lHandRest;
            if (rightHand != null) rightHand.localRotation = rHandRest;
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

        // Sink down + deflate scale + all bones slump
        float shrink = 0.88f;
        yield return ScaleOverTime(originalScale * shrink, 0.4f);
        if (spine != null) yield return RotateBoneOverTime(spine, sRest, sCurl, 0.3f);
        if (chest != null) chest.localRotation = cCave;
        if (head != null) yield return RotateBoneOverTime(head, hRest, hDrop, 0.25f);
        if (leftArm != null) leftArm.localRotation = lDroop;
        if (rightArm != null) rightArm.localRotation = rDroop;
        if (leftHand != null) leftHand.localRotation = lhDangle;
        if (rightHand != null) rightHand.localRotation = rhDangle;

        yield return new WaitForSeconds(0.8f);

        // Recover
        if (head != null) yield return RotateBoneOverTime(head, hDrop, hRest, 0.3f);
        if (spine != null) yield return RotateBoneOverTime(spine, sCurl, sRest, 0.3f);
        if (chest != null) chest.localRotation = cRest;
        if (leftArm != null) leftArm.localRotation = lRest;
        if (rightArm != null) rightArm.localRotation = rRest;
        if (leftHand != null) leftHand.localRotation = lhRest;
        if (rightHand != null) rightHand.localRotation = rhRest;
        yield return ScaleOverTime(originalScale, 0.4f);

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
            // Jump up + arms raise + head back
            yield return MoveOverTime(Vector3.up, jumpHeight + 0.04f, upSpeed);
            if (leftArm != null) leftArm.localRotation = lUp;
            if (rightArm != null) rightArm.localRotation = rUp;
            if (leftLower != null) leftLower.localRotation = llRest * Quaternion.Euler(-15f, 0f, 0f);
            if (rightLower != null) rightLower.localRotation = rlRest * Quaternion.Euler(-15f, 0f, 0f);
            if (head != null) head.localRotation = hBack;
            if (spine != null) spine.localRotation = sArch;
            // Fall down + arms return
            yield return MoveOverTime(Vector3.up, -jumpHeight, downSpeed);
            if (leftArm != null) leftArm.localRotation = lRest;
            if (rightArm != null) rightArm.localRotation = rRest;
            if (leftLower != null) leftLower.localRotation = llRest;
            if (rightLower != null) rightLower.localRotation = rlRest;
            if (head != null) head.localRotation = hRest;
            if (spine != null) spine.localRotation = sRest;
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
        if (idleAnimator != null) idleAnimator.paused = true;
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

        float beatTime = 0.18f;
        float bounceHeight = 0.10f;

        for (int i = 0; i < 8; i++)
        {
            float dir = (i % 2 == 0) ? 1f : -1f;

            // Spine sway + head bob + arms pump alternating
            if (spine != null) spine.localRotation = sRest * Quaternion.Euler(-5f, 8f * dir, 15f * dir);
            if (head != null) head.localRotation = hRest * Quaternion.Euler(-8f, -5f * dir, 10f * dir);

            // One arm up, one arm down — alternating pump
            if (i % 2 == 0)
            {
                if (leftArm != null) leftArm.localRotation = lRest * Quaternion.Euler(-60f, 0f, 15f);
                if (leftLower != null) leftLower.localRotation = llRest * Quaternion.Euler(-70f, 0f, 0f);
                if (rightArm != null) rightArm.localRotation = rRest * Quaternion.Euler(-15f, 0f, -8f);
                if (rightLower != null) rightLower.localRotation = rlRest * Quaternion.Euler(-20f, 0f, 0f);
            }
            else
            {
                if (rightArm != null) rightArm.localRotation = rRest * Quaternion.Euler(-60f, 0f, -15f);
                if (rightLower != null) rightLower.localRotation = rlRest * Quaternion.Euler(-70f, 0f, 0f);
                if (leftArm != null) leftArm.localRotation = lRest * Quaternion.Euler(-15f, 0f, 8f);
                if (leftLower != null) leftLower.localRotation = llRest * Quaternion.Euler(-20f, 0f, 0f);
            }

            // Hands wiggle
            if (leftHand != null) leftHand.localRotation = lhRest * Quaternion.Euler(0f, 0f, 20f * dir);
            if (rightHand != null) rightHand.localRotation = rhRest * Quaternion.Euler(0f, 0f, -20f * dir);

            // Bounce
            yield return MoveOverTime(Vector3.up, bounceHeight, beatTime * 0.5f);
            yield return MoveOverTime(Vector3.up, -bounceHeight, beatTime * 0.5f);
        }

        // Reset all bones
        if (head != null) head.localRotation = hRest;
        if (spine != null) spine.localRotation = sRest;
        if (leftArm != null) leftArm.localRotation = lRest;
        if (rightArm != null) rightArm.localRotation = rRest;
        if (leftLower != null) leftLower.localRotation = llRest;
        if (rightLower != null) rightLower.localRotation = rlRest;
        if (leftHand != null) leftHand.localRotation = lhRest;
        if (rightHand != null) rightHand.localRotation = rhRest;

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

        // Resume idle bone animations
        if (idleAnimator != null)
            idleAnimator.paused = false;

        isPlaying = false;
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
