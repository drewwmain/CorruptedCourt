using UnityEngine;

/// <summary>
/// The behavioural stand-in a <see cref="PartnerMinigame"/> talks to when there's no real second
/// player (solo testing). Plays its half of the shared animation and auto-"accepts" after a delay so
/// handshake / cheers / dance / duel all complete single-player.
///
/// Put this on a simple humanoid prefab (a spare Synty character). Pair it with the existing
/// <c>DummyTestHelper</c> if the stand-in needs to be holding something (wine glass, sword).
/// </summary>
public class DummyPartner : MonoBehaviour
{
    private Animator anim;
    private float acceptAt = -1f;

    /// <summary>True once the stand-in has "agreed" to the interaction.</summary>
    public bool HasAccepted { get; private set; }

    private void Awake()
    {
        anim = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (!HasAccepted && acceptAt >= 0f && Time.time >= acceptAt)
            HasAccepted = true;
    }

    /// <summary>Start the countdown to auto-accept.</summary>
    public void BeginAutoAccept(float delaySeconds)
    {
        acceptAt = Time.time + Mathf.Max(0f, delaySeconds);
    }

    /// <summary>Play an animation trigger on the stand-in (mirrors the initiator's clip).</summary>
    public void Play(string trigger)
    {
        if (anim != null && !string.IsNullOrEmpty(trigger)) anim.SetTrigger(trigger);
    }

    /// <summary>Hold a looping pose (dance / conversation idle).</summary>
    public void HoldPose(string boolParam, bool on)
    {
        if (anim != null && !string.IsNullOrEmpty(boolParam)) anim.SetBool(boolParam, on);
    }

    /// <summary>Remove the stand-in when the minigame ends.</summary>
    public void Dismiss()
    {
        Destroy(gameObject);
    }
}
