using UnityEngine;
using CorruptedCourt.Core;
using CorruptedCourt.Items;

namespace CorruptedCourt.Gameplay
{
    // This script is strictly for Editor testing and can be removed before final build
    [RequireComponent(typeof(PlayerController))]
    public class DummyTestHelper : MonoBehaviour
    {
        [Header("Test State Injection")]
        [Tooltip("Drag the item prefab here that you want the dummy to hold on start.")]
        public PickupItem itemToEquip;

        void Start()
        {
            if (itemToEquip != null)
            {
                PlayerController player = GetComponent<PlayerController>();

                // 1. Create a fresh physical copy of the item in the world (identity rides on 'definition',
                //    which Instantiate copies - no name fix-up needed for matching).
                PickupItem clone = Instantiate(itemToEquip);

                // 2. Force the dummy player to equip it using your existing public method
                player.EquipItem(clone);

                Log.Game($"[Test Harness] Forced {gameObject.name} to equip {clone.DisplayName}");
            }
        }
    }
}
