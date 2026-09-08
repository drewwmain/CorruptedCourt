using UnityEngine;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// "Place the bouquet in the vase" deposit minigame - the lid-less sibling of ChestDepositMinigame.
    ///
    /// The held bouquet is posed in the RIGHT hand; the player is frozen and aims with the mouse.
    /// LEFT-CLICK lets go - it falls dead-straight (rotation + X/Z frozen, no bounce). It deposits the
    /// instant its designated stem-base contact point physically reaches the interior-bottom
    /// DepositTarget volume of a free vase slot. A miss leaves it loose on the ground; walk over and
    /// pick it back up (E) to aim again. RIGHT-CLICK (before releasing) cancels and hands it back.
    ///
    /// Since P5 the whole release / settle / retry / abandon / cancel-to-hand lifecycle and the
    /// contact-driven deposit gate live in ItemDepositMinigame. This class keeps only: the carry pose,
    /// the frozen dead-straight drop, and making the released bouquet pass through the vase's own solid
    /// colliders while it falls (so the vase needs no modelled hollow - the DepositTarget volume is the
    /// only gate). The player still collides with the vase normally. See ARCHITECTURE.md.
    /// </summary>
    public class VaseDepositMinigame : ItemDepositMinigame
    {
        [Header("Bouquet in hand")]
        public Vector3 carryLocalPos = Vector3.zero;
        public Vector3 carryLocalEuler = Vector3.zero;

        private Collider[] passableVaseColliders;

        // --- HandMinigame gates: mirrors ChestDepositMinigame minus the lid phases. The minigame keeps
        // its own look active until the deposit resolves; footwork / free cursor / tap-cancel switch off
        // the moment the bouquet is released (normal control is handed back to the player then).
        protected override bool LookActive => !resolving;
        protected override bool FootworkActive => !resolving && !released;
        protected override bool AllowTapCancel() => !released;
        protected override bool WantsFreeCursor => !released;

        // Pose the bouquet in the right hand, kinematic, collider off. Called from BeginDeposit and
        // RestartAiming.
        protected override void PoseCarriedItem()
        {
            Transform hb = Hand.HandBone;
            if (Item == null || hb == null) return;

            Item.transform.SetParent(hb, false);
            Item.transform.localPosition = carryLocalPos;
            Item.transform.localRotation = Quaternion.Euler(carryLocalEuler);

            Rigidbody rb = Item.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            Collider c = Item.GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }

        // Dead-straight drop: rotation AND X/Z position frozen, no bounce, gentle depenetration. The
        // bouquet falls from exactly where the player let go - aim is entirely theirs.
        protected override GuidedDrop.Settings DropSettings() => new GuidedDrop.Settings
        {
            maxAngularVelocity = 2.5f,
            maxDepenetrationVelocity = 0.5f,
            freezeRotation = true,
            freezeHorizontalPosition = true,
            funnelSpeed = 0f,
            materialName = "VaseDrop"
        };

        // While the bouquet is a falling physics object, make it ignore the vase's own solid colliders so
        // it can reach the interior-bottom DepositTarget volume even when the vase mesh has no modelled
        // opening. The player still collides with the vase.
        protected override void OnReleased() => SetVasePassable(true);

        protected override void OnDeposited(int slot)
        {
            SetVasePassable(false);   // restore bouquet <-> vase collision while Item is still referenced
            base.OnDeposited(slot);   // Item = null; CompleteMinigame();
        }

        protected override void OnCleanup() => SetVasePassable(false);

        // The player grabbed the bouquet back: clear the pass-through before the base re-poses and re-locks.
        protected override void RestartAiming()
        {
            SetVasePassable(false);
            base.RestartAiming();
        }

        private void SetVasePassable(bool passable)
        {
            if (passable) passableVaseColliders = Station != null ? Station.GetComponentsInChildren<Collider>() : null;
            if (passableVaseColliders == null) return;

            Collider itemCol = Item != null ? Item.GetComponent<Collider>() : null;
            if (itemCol != null)
            {
                foreach (Collider c in passableVaseColliders)
                {
                    if (c == null || c.isTrigger) continue;
                    Physics.IgnoreCollision(itemCol, c, passable);
                }
            }

            if (!passable) passableVaseColliders = null;
        }
    }
}
