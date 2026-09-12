using UnityEngine;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Runtime record of what a container-type PickupItem currently holds (e.g. a Quiver's arrows,
    /// a Plate's cake). Sits beside PickupItem on the same GameObject. Written by
    /// RoundRoleSwitch.TransferDepositedItems and, later, the pour minigame; read by
    /// ConsumeItemStep and AcquireItemStep. Runtime scene-object state, so it lives in Gameplay
    /// (beside PickupItem) rather than Items - see ARCHITECTURE.md §6.3.
    /// </summary>
    public class ItemPayload : MonoBehaviour
    {
        [Tooltip("What the container currently holds. Null = empty.")]
        public ItemDefinition contents;

        [Tooltip("How many servings/units of 'contents' are currently held.")]
        public int count;

        [Tooltip("Maximum count this container can hold (informational for now - e.g. Quiver = 10).")]
        public int capacity = 1;
    }
}
