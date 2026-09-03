using System;

/// <summary>
/// Runtime state flags for a <see cref="PickupItem"/>. Replaces the string-prefix encoding
/// (<c>"Processed"</c> / <c>"Deposited"</c> prepended to <c>itemName</c>), which was order-dependent
/// and could not be extended.
/// </summary>
[Flags]
public enum ItemState
{
    None = 0,
    Processed = 1 << 0,
    DepositedContainer = 1 << 1,
    Spent = 1 << 2,
}
