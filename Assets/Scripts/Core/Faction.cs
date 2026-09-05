namespace CorruptedCourt.Core
{
    /// <summary>
    /// Which side a player fights for. Assigned once at match start by <c>RoleManager</c> and
    /// immutable for the rest of the match - an appointment, demotion or succession must never
    /// change it.
    ///
    /// Faction governs: kill abilities, power-up pickup and use, whether a completed task credits
    /// the global Court meter, and both population win conditions. A Corrupted player who is
    /// appointed Kingsguard keeps <see cref="Corrupted"/> here.
    ///
    /// Lives in Core so item, task and UI code can gate on it without depending on the Gameplay
    /// assembly. Serialized as its underlying int, so reordering the members would break saved
    /// data - append only.
    /// </summary>
    public enum Faction
    {
        Court,
        Corrupted
    }
}
