using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerIKHelper : MonoBehaviour
{
    [Tooltip("Drag your main Player object (the one with the PlayerController script) in here.")]
    public PlayerController mainController;

    // Because THIS script sits next to the Animator, Unity will successfully fire this method!
    private void OnAnimatorIK(int layerIndex)
    {
        if (mainController != null)
        {
            // Tell the PlayerController to run its IK math.
            // Strangle IK runs last so it wins the hand goals while a strangle is active.
            mainController.ApplyMinigameIK(layerIndex);
            mainController.ApplyStrangleIK(layerIndex);
            mainController.ApplyHangReachIK(layerIndex);
            mainController.ApplyHaulIK(layerIndex);
        }
    }
}