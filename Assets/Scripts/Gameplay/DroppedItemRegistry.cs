using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Tracks each player's one outstanding manually-dropped item (ARCHITECTURE.md §9.2, R5).
    /// ItemLifecycle.Drop is the only place that decides to despawn a replaced drop - this class is
    /// plain state plus the fade-out visual, not the R5 business rule itself. Singleton pattern
    /// matches RoleManager/TaskManager (Awake sets Instance, else Destroy; Instance is never cleared).
    /// </summary>
    public class DroppedItemRegistry : MonoBehaviour
    {
        public static DroppedItemRegistry Instance { get; private set; }

        [Tooltip("Seconds the shrink-and-vanish takes when a replaced drop is despawned.")]
        [SerializeField] private float fadeDuration = 0.35f;

        private readonly Dictionary<PlayerController, PickupItem> outstandingDrops = new Dictionary<PlayerController, PickupItem>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        /// <summary>Records <paramref name="item"/> as <paramref name="dropper"/>'s outstanding drop.
        /// No-op for a Forced cause (R5: the pre-meeting scramble never consumes the one-drop slot).</summary>
        public void RegisterDrop(PlayerController dropper, PickupItem item, DropCause cause)
        {
            if (dropper == null || item == null) return;
            if (cause == DropCause.Forced) return;

            outstandingDrops[dropper] = item;
        }

        /// <summary>Called by anyone picking up a dropped item, so its dropper's slot frees up.</summary>
        public void Clear(PickupItem item)
        {
            if (item == null) return;

            PlayerController key = null;
            foreach (KeyValuePair<PlayerController, PickupItem> kvp in outstandingDrops)
            {
                if (kvp.Value == item) { key = kvp.Key; break; }
            }
            if (key != null) outstandingDrops.Remove(key);
        }

        /// <summary>The item <paramref name="p"/> currently has outstanding on the floor, or null.</summary>
        public PickupItem OutstandingFor(PlayerController p)
        {
            if (p == null) return null;
            return outstandingDrops.TryGetValue(p, out PickupItem item) ? item : null;
        }

        /// <summary>Despawns <paramref name="item"/> with a brief shrink instead of an instant pop, per
        /// §9.2 ("visible - a fade or puff, not an instant vanish, or it reads as a bug"). Disables the
        /// collider first so it can't be picked up mid-fade (which would otherwise let
        /// ItemLifecycle.Despawn destroy an item a player is now holding).</summary>
        public void DespawnWithFade(PickupItem item)
        {
            if (item == null) return;
            StartCoroutine(FadeThenDespawn(item));
        }

        private IEnumerator FadeThenDespawn(PickupItem item)
        {
            Collider col = item.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            Transform t = item.transform;
            Vector3 startScale = t.localScale;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                if (item == null) yield break; // destroyed by something else mid-fade
                elapsed += Time.deltaTime;
                t.localScale = Vector3.Lerp(startScale, Vector3.zero, elapsed / fadeDuration);
                yield return null;
            }

            if (item != null) ItemLifecycle.Despawn(item);
        }
    }
}
