using UnityEngine;
using CorruptedCourt.Gameplay;
using CorruptedCourt.Items;

namespace CorruptedCourt.Minigames
{
    /// <summary>
    /// One effect a producing minigame applies on success (Tasks/ARCHITECTURE.md §9.4): flag the held
    /// item, spawn a new item to carry off, spawn one straight into the hand, or fill a container's
    /// payload. Authored as a <c>List&lt;MinigameProduct&gt;</c>, not a single entry - the pour
    /// minigame needs two effects at once: <c>FillContainerStation(Wine)</c> on the glass and
    /// <c>addFlags = Spent</c> on the held pitcher.
    ///
    /// Mutates only through <see cref="ItemLifecycle"/> (B4) - never <c>PickupItem</c> fields
    /// directly - and folds <see cref="SpawnAndCarryObjective"/> (already existed, §III.1) in as the
    /// two spawning modes instead of duplicating spawn/offset/carry-hint logic.
    /// </summary>
    [System.Serializable]
    public class MinigameProduct
    {
        public enum Mode
        {
            /// <summary>Adds <see cref="addFlags"/> to <c>targetItem</c> (e.g. polish -> Processed).</summary>
            FlagHeldItem,
            /// <summary>Spawns <see cref="spawnPrefab"/> near <c>spawnAnchor</c> and pushes a carry hint
            /// (cut cake/turkey -> a piece the player must carry to the plate).</summary>
            SpawnAtStation,
            /// <summary>Spawns <see cref="spawnPrefab"/> directly into the player's hand - no carrying needed.</summary>
            SpawnIntoHand,
            /// <summary>Sets an <c>ItemPayload</c> on <c>targetItem</c> (e.g. pour -> the glass becomes "Wine ×1").</summary>
            FillContainerStation
        }

        public Mode mode;

        [Tooltip("FlagHeldItem only: state flags added to the target item.")]
        public ItemState addFlags;

        [Tooltip("SpawnAtStation / SpawnIntoHand only: the item produced.")]
        public PickupItem spawnPrefab;

        [Tooltip("FillContainerStation only: the payload contents set on the target item.")]
        public ItemDefinition payloadContents;
        [Tooltip("FillContainerStation only: the payload count set on the target item.")]
        public int payloadCount = 1;

        [Tooltip("After applying, despawn the item passed as targetItem (e.g. the whole cake, once a piece is cut from it).")]
        public bool consumeSourceItem;

        /// <summary>
        /// Applies this effect. <paramref name="targetItem"/> is what FlagHeldItem / FillContainerStation
        /// act on, and what gets despawned afterward if <see cref="consumeSourceItem"/> is set - which
        /// item that is (the active hand, the off hand, a station's already-deposited item, ...) is the
        /// calling minigame's call, since it varies (pour needs the pitcher for one entry and the glass
        /// for another, both from the same player, one <see cref="Apply"/> call each).
        /// <paramref name="spawnAnchor"/> is where SpawnAtStation spawns near - ignored by every other mode.
        /// </summary>
        public void Apply(PlayerController player, PickupItem targetItem, Transform spawnAnchor)
        {
            switch (mode)
            {
                case Mode.FlagHeldItem:
                    ItemLifecycle.SetState(targetItem, addFlags, ItemState.None);
                    break;

                case Mode.FillContainerStation:
                    ItemLifecycle.SetPayload(targetItem, payloadContents, payloadCount);
                    break;

                case Mode.SpawnAtStation:
                {
                    var objective = new SpawnAndCarryObjective { producedPrefab = spawnPrefab };
                    objective.Produce(player, spawnAnchor);
                    objective.PushCarryHint(player);
                    break;
                }

                case Mode.SpawnIntoHand:
                {
                    if (spawnPrefab == null || player == null) break;
                    PickupItem spawned = ItemLifecycle.Spawn(spawnPrefab,
                        new ItemIdentity { definition = spawnPrefab.definition, state = spawnPrefab.state },
                        player.transform.position, Quaternion.identity);
                    ItemLifecycle.GiveToHand(player, spawned, Hand.Right);
                    break;
                }
            }

            if (consumeSourceItem && targetItem != null) ItemLifecycle.Despawn(targetItem);
        }
    }
}
