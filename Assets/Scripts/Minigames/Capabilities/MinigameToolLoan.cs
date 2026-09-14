using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// Loans a tool for a minigame's duration (Tasks/ARCHITECTURE.md §10.4): stashes the player's
    /// active hand (and, if <see cref="stashHeldItems"/>, the off hand too), spawns and equips
    /// <see cref="toolPrefab"/>, then on <see cref="End"/> despawns the tool and restores whatever was
    /// stashed - to the SAME hands, via the same ordering trick <see cref="PlayerInventory.EquipItem"/>
    /// already uses (an empty active hand takes priority, so restoring the former active-hand item
    /// first then the off-hand item second lands each back where it came from).
    ///
    /// Stashing (hide the GameObject + clear the hand reference) rather than dropping matters even
    /// without decay: force-dropping a polished sword to light a candle would consume the player's one
    /// drop slot (R5, DroppedItemRegistry). <c>PlayerInventory.itemSwappedToLeftHand</c> /
    /// <c>ReturnSwappedItem</c> is the existing single-item version of "set aside, restore later" this
    /// generalises from - same restore-via-ItemLifecycle idea, extended to both hands and a spawned
    /// loaner instead of a reposition.
    ///
    /// Known gap, not handled: if <see cref="toolPrefab"/> is heavy / two-handed and
    /// <see cref="stashHeldItems"/> is off, PlayerInventory.EquipItem's own two-handed branch will drop
    /// the un-stashed off-hand item rather than stash it. Author heavy tools with stashHeldItems on.
    /// </summary>
    [System.Serializable]
    public class MinigameToolLoan
    {
        [Tooltip("The tool spawned into the player's active hand for the minigame's duration.")]
        public PickupItem toolPrefab;
        [Tooltip("Also stash the off-hand item (not just the active hand). Off if the minigame's own " +
                 "target item should stay in the player's other hand during the loan.")]
        public bool stashHeldItems = true;

        private PlayerController player;
        private PickupItem stashedRight;
        private PickupItem stashedLeft;
        private PickupItem loanedTool;

        /// <summary>Stashes hand(s), spawns <see cref="toolPrefab"/>, and equips it into the now-empty active hand.</summary>
        public void Begin(PlayerController p)
        {
            if (p == null) return;
            player = p;

            stashedRight = player.GetHeldItem();
            if (stashedRight != null)
            {
                stashedRight.gameObject.SetActive(false);
                player.ClearHeldItem();
            }

            if (stashHeldItems)
            {
                stashedLeft = player.GetLeftHeldItem();
                if (stashedLeft != null)
                {
                    stashedLeft.gameObject.SetActive(false);
                    player.ClearLeftHeldItem();
                }
            }

            if (toolPrefab == null) return;

            loanedTool = ItemLifecycle.Spawn(toolPrefab,
                new ItemIdentity { definition = toolPrefab.definition, state = toolPrefab.state },
                player.transform.position, Quaternion.identity);
            player.EquipItem(loanedTool); // active hand was just vacated above, so this is where it lands
        }

        /// <summary>Despawns the loaned tool and restores whatever was stashed. Safe on both success and cancel.</summary>
        public void End()
        {
            if (loanedTool != null)
            {
                if (player != null) player.ClearHeldItem(); // currently the tool's own reference - drop it before Destroy
                ItemLifecycle.Despawn(loanedTool);
                loanedTool = null;
            }

            if (player != null)
            {
                // Right first: with both hands empty, EquipItem's own logic lands it back in the active
                // hand; the left restore then finds the active hand full and off-hand empty, landing there.
                if (stashedRight != null) { stashedRight.gameObject.SetActive(true); player.EquipItem(stashedRight); }
                if (stashedLeft != null) { stashedLeft.gameObject.SetActive(true); player.EquipItem(stashedLeft); }
            }

            stashedRight = null;
            stashedLeft = null;
            player = null;
        }
    }
}
