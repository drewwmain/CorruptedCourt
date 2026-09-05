using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    public class RoyalWeapon : PickupItem
    {
        [Header("Royal Combat Setup")]
        [Tooltip("Who is allowed to pick this up? (e.g., King or Kingsguard)")]
        public PlayerRole restrictedRole;

        [Tooltip("Time in seconds the Royal remains in a blocking state.")]
        public float blockDuration = 2.0f;
    }
}
