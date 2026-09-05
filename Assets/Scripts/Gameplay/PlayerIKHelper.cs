using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    [RequireComponent(typeof(Animator))]
    public class PlayerIKHelper : MonoBehaviour
    {
        [Tooltip("Drag your main Player object (the one with the PlayerController script) in here.")]
        public PlayerController mainController;

        // PlayerIKRig and PlayerStrangle live on the same (root) GameObject as mainController, not here -
        // resolved once via mainController rather than requiring extra manual drags in the Inspector.
        private PlayerIKRig ikRig;
        private PlayerStrangle strangle;

        void Awake()
        {
            if (mainController != null)
            {
                ikRig = mainController.GetComponent<PlayerIKRig>();
                strangle = mainController.GetComponent<PlayerStrangle>();
            }
        }

        // Because THIS script sits next to the Animator, Unity will successfully fire this method!
        private void OnAnimatorIK(int layerIndex)
        {
            if (mainController != null)
            {
                // Tell PlayerController / PlayerIKRig to run their IK math.
                // Strangle IK runs last so it wins the hand goals while a strangle is active.
                ikRig?.ApplyMinigameIK(layerIndex);
                strangle?.ApplyStrangleIK(layerIndex);
                mainController.ApplyHangReachIK(layerIndex);
                ikRig?.ApplyHaulIK(layerIndex);
            }
        }
    }
}
