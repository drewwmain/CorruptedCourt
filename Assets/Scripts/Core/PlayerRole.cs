namespace CorruptedCourt.Core
{
    /// <summary>
    /// LEGACY combined role. Live player alignment is now split across two orthogonal axes:
    /// <see cref="Faction"/> (which team you fight for) and <see cref="CourtTitle"/> (which office
    /// you hold). This enum survives only as the authoring value for <c>RoleManager.forceTestRole</c>,
    /// the Inspector test knob that forces the local player's starting role - <c>RoleManager</c>
    /// maps each member onto a Faction + CourtTitle pair at match start.
    ///
    /// Lives in Core so item, task, and UI code can reference it without depending on the Gameplay
    /// assembly. Serialized as its underlying int, so reordering the members would break saved
    /// data (and the SampleScene forceTestRole override) - append only.
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
