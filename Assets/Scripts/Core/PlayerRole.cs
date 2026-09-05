namespace CorruptedCourt.Core
{
    /// <summary>
    /// The role a player holds for the current round. Lives in Core so item, task, and UI code can
    /// gate on it without depending on the Gameplay assembly. Serialized as its underlying int, so
    /// reordering the members would break saved data - append only.
    /// </summary>
    public enum PlayerRole
    {
        None,
        King,
        Kingsguard,
        Court,
        Corrupted
    }
}
