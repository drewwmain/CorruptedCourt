using UnityEngine;

public class FirstPersonHeadHider : MonoBehaviour
{
    [Header("Local Player Check")]
    [Tooltip("Uncheck this for Dummy Players and network clones!")]
    public bool isLocalPlayer = true;

    [Header("Bone References")]
    [Tooltip("Drag the Synty character's Head bone here")]
    public Transform headBone;
    
    [Tooltip("Drag the Synty character's Neck bone here")]
    public Transform neckBone; 

    void LateUpdate()
    {
        if (isLocalPlayer)
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