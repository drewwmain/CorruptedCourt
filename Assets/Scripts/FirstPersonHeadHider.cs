using UnityEngine;

public class FirstPersonHeadHider : MonoBehaviour
{
    [Header("Local Player Check")]
    [Tooltip("Fallback only. When a PlayerController is found on a parent, its IsLocal is used instead " +
             "of this. Only matters for a standalone head with no PlayerController above it.")]
    public bool isLocalPlayer = true;

    [Header("Bone References")]
    [Tooltip("Drag the Synty character's Head bone here")]
    public Transform headBone;

    [Tooltip("Drag the Synty character's Neck bone here")]
    public Transform neckBone;

    private PlayerController owner;

    void Awake()
    {
        owner = GetComponentInParent<PlayerController>();
    }

    void LateUpdate()
    {
        // Prefer the owning PlayerController's identity; fall back to the local field if there's none.
        bool local = owner != null ? owner.IsLocal : isLocalPlayer;

        if (local)
        {
            // Crush both bones to zero so the entire neck and head vanish for YOUR camera
            if (headBone != null) headBone.localScale = Vector3.zero;
            if (neckBone != null) neckBone.localScale = Vector3.zero;
        }
        else
        {
            // Ensure the head is fully visible (scale of 1) for everyone else!
            if (headBone != null) headBone.localScale = Vector3.one;
            if (neckBone != null) neckBone.localScale = Vector3.one;
        }
    }
}
