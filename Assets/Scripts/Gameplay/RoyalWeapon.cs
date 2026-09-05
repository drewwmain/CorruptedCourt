using UnityEngine;
using CorruptedCourt.Core;

namespace CorruptedCourt.Gameplay
{
    public class RoyalWeapon : PickupItem
    {
        [Header("Royal Combat Setup")]
        [Tooltip("Which court title is allowed to pick this up (King or Kingsguard). Checked against the " +
                 "picker's CourtTitle, not their Faction - a Corrupted officer may still wield it.")]
        public CourtTitle restrictedRole;

        [Tooltip("Time in seconds the Royal remains in a blocking state.")]
        public float blockDuration = 2.0f;
    }
}
