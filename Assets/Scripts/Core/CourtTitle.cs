namespace CorruptedCourt.Core
{
    /// <summary>
    /// The office a player currently holds at court, independent of their <see cref="Faction"/>.
    /// Mutable: set by royal appointment or succession. Defaults to <see cref="None"/>.
    ///
    /// Title governs: arrest authority, appointment authority, royal weapon eligibility, the royal
    /// bonus HP, and whether the player receives tasks. A Corrupted player can hold a title without
    /// their Faction changing.
    ///
    /// Lives in Core so item, task and UI code can gate on it without depending on the Gameplay
    /// assembly. Serialized as its underlying int - append only, never reorder. The values
    /// None/King/Kingsguard = 0/1/2 line up with the old <see cref="PlayerRole"/> members so
    /// existing <c>RoyalWeapon.restrictedRole</c> asset data survives the type change.
    /// </summary>
    public enum CourtTitle
    {
        None,
        King,
        Kingsguard
    }
}
