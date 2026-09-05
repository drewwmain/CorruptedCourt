using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerIKHelper : MonoBehaviour
{
    [Tooltip("Drag your main Player object (the one with the PlayerController script) in here.")]
    public PlayerController mainController;

    // PlayerIKRig lives on the same (root) GameObject as mainController, not here - resolved once
    // via mainController rather than requiring a second manual drag in the Inspector.
    private PlayerIKRig ikRig;

    void Awake()
    {
        if (mainController != null) ikRig = mainController.GetComponent<PlayerIKRig>();
    }

    // Because THIS script sits next to the Animator, Unity will successfully fire this method!
    private void OnAnimatorIK(int layerIndex)
    {
        if (mainController != null)
        {
            // Tell PlayerController / PlayerIKRig to run their IK math.
            // Strangle IK runs last so it wins the hand goals while a strangle is active.
            ikRig?.ApplyMinigameIK(layerIndex);
            mainController.Vitals.ApplyStrangleIK(layerIndex);
            mainController.ApplyHangReachIK(layerIndex);
            ikRig?.ApplyHaulIK(layerIndex);
        }
    }
}