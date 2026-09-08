using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Marks the logical target location an item's <see cref="DepositContactPoint"/> must reach for a
    /// contact-driven deposit - a rack notch seat, the interior floor of a chest, the interior bottom
    /// of a vase. Sits on a <c>DropSlot</c> transform of a <see cref="TaskDepositStation"/> (or a
    /// child of it).
    ///
    /// <para>Authoring marker only: no <c>Update</c>, no runtime state. Nothing consumes it yet -
    /// contact-driven deposits are wired in a later step.</para>
    /// </summary>
    public class DepositTarget : MonoBehaviour
    {
        [Tooltip("The trigger volume the contact point must reach. If left empty, a Collider on this " +
                 "same GameObject is used instead.")]
        [SerializeField] private Collider volume;

        /// <summary>
        /// The trigger volume for this target: the assigned <see cref="volume"/> when set, otherwise a
        /// Collider on the same GameObject. Null when neither exists.
        /// </summary>
        public Collider Volume => volume != null ? volume : GetComponent<Collider>();

        // Authoring-time guard. Uses Debug.LogWarning (not Log.Warn) on purpose: a misconfiguration
        // must surface in the Editor regardless of the CC_LOGGING symbol.
        private void OnValidate()
        {
            Collider v = Volume;

            if (v == null)
            {
                Debug.LogWarning($"[DepositTarget] {name}: no trigger volume - assign one, or add a " +
                                 "Collider to this GameObject.", this);
                return;
            }

            if (!v.isTrigger)
                Debug.LogWarning($"[DepositTarget] {name}: volume ({v.GetType().Name}) is not set to " +
                                 "Is Trigger.", this);

            if (v is MeshCollider mesh && !mesh.convex)
                Debug.LogWarning($"[DepositTarget] {name}: volume is a non-convex MeshCollider - " +
                                 "ClosestPoint is unreliable on those. Use a Box, Sphere, Capsule, or " +
                                 "convex MeshCollider.", this);
        }

        private void OnDrawGizmosSelected()
        {
            Collider v = Volume;
            if (v == null) return;

            Gizmos.color = new Color(0.25f, 0.7f, 1f, 1f);
            Bounds b = v.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
