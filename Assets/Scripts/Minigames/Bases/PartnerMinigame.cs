using UnityEngine;

/// <summary>
/// Base for "do something WITH another court member": handshake, cheers, dance, conversation, duel.
///
/// On begin it resolves a <see cref="Partner"/> - the real player the initiator aimed at, or (in a
/// solo test) a <see cref="DummyPartner"/> spawned in front of them that plays its half of the
/// animation and auto-accepts. Subclasses implement the actual success test in
/// <see cref="OnMinigameUpdate"/> and call <see cref="CompleteMinigame"/>.
/// </summary>
public abstract class PartnerMinigame : MinigameBase
{
    [Header("Partner")]
    [Tooltip("Spawned as the stand-in partner when the player isn't aimed at a real court member (solo testing).")]
    public GameObject dummyPartnerPrefab;
    [Tooltip("How far in front of the initiator the stand-in partner is placed.")]
    public float dummyPartnerDistance = 1.4f;
    [Tooltip("Seconds before the stand-in partner auto-accepts, so solo tests always complete.")]
    public float dummyAutoAcceptDelay = 1.25f;

    /// <summary>The other participant (real player or stand-in).</summary>
    protected PlayerController Partner { get; private set; }

    /// <summary>True when <see cref="Partner"/> is an AI stand-in rather than a real player.</summary>
    protected bool PartnerIsDummy { get; private set; }

    private DummyPartner dummy;

    protected override void OnMinigameBegin()
    {
        Partner = PartnerResolver.Resolve(Context, player, dummyPartnerPrefab,
                                          dummyPartnerDistance, out dummy);
        PartnerIsDummy = dummy != null;

        if (Partner == null)
        {
            Debug.LogWarning($"[{GetType().Name}] no partner and no dummy prefab - cancelling.");
            CancelMinigame();
            return;
        }

        FacePartner();
        if (PartnerIsDummy) dummy.BeginAutoAccept(dummyAutoAcceptDelay);
        OnPartnerBegin();
    }

    protected override void OnMinigameEnd(bool won)
    {
        if (dummy != null) dummy.Dismiss();
    }

    private void Update()
    {
        if (player == null || Partner == null) return;
        OnMinigameUpdate();
    }

    /// <summary>Turn the initiator's body to face the partner (yaw only).</summary>
    protected void FacePartner()
    {
        Vector3 dir = Partner.transform.position - player.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            player.transform.rotation = Quaternion.LookRotation(dir);
    }

    /// <summary>Play the same animation trigger on both sides (handshake, cheers clink, ...).</summary>
    protected void MirrorOnPartner(string animTrigger)
    {
        if (PartnerIsDummy && dummy != null) dummy.Play(animTrigger);
        // TODO(P5): real remote players get the trigger over the network / via their PlayerController.
    }

    /// <summary>True once the dummy has auto-accepted (or immediately for a cooperating real player).</summary>
    protected bool PartnerAccepted => PartnerIsDummy ? (dummy != null && dummy.HasAccepted) : true;

    // --- hooks --------------------------------------------------------------------------------
    protected virtual void OnPartnerBegin() { }
    protected virtual void OnMinigameUpdate() { }
}
