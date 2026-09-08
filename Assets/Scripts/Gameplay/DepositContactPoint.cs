using UnityEngine;

namespace CorruptedCourt.Gameplay
{
    /// <summary>
    /// Marks the exact point on a <see cref="PickupItem"/> that must physically land inside a deposit
    /// station for a contact-driven deposit to register - a sword's guard, a bag's base, a bouquet's
    /// stem base. Sits on a child transform of the item, placed at that point.
    ///
    /// <para>Authoring marker only: no <c>Update</c>, no runtime state. Nothing consumes it yet -
    /// contact-driven deposits are wired in a later step.</para>
    /// </summary>
    public class DepositContactPoint : MonoBehaviour
    {
        [Tooltip("How close (metres) this point must get to a DepositTarget volume to count as contact.")]
        public float contactRadius = 0.03f;

        /// <summary>World-space position of the contact point (this transform's position).</summary>
        public Vector3 WorldPosition => transform.position;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, contactRadius);
        }
    }
}
