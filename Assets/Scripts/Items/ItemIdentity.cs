namespace CorruptedCourt.Items
{
    /// <summary>
    /// Pure-data pairing of an item's definition and required state flags. Match logic lives on
    /// <c>PickupItem.Matches(ItemIdentity)</c> in the Gameplay assembly instead of here - Items
    /// cannot reference Gameplay, so this struct carries no methods (see ARCHITECTURE.md §6.1 and
    /// the assembly-placement correction in IMPLEMENTATION_PROMPTS.md).
    /// </summary>
    [System.Serializable]
    public struct ItemIdentity
    {
        public ItemDefinition definition;
        public ItemState state;
    }
}
