using UnityEngine;

/// <summary>
/// Decides who the "other player" in a PartnerMinigame is:
///  1. the real court member the initiator aimed at (Context.PartnerPlayer), or
///  2. a spawned <see cref="DummyPartner"/> stand-in for solo testing.
/// </summary>
public static class PartnerResolver
{
    public static PlayerController Resolve(MinigameContext context, PlayerController initiator,
                                          GameObject dummyPrefab, float dummyDistance,
                                          out DummyPartner dummy)
    {
        dummy = null;

        PlayerController real = context != null ? context.PartnerPlayer : null;
        if (real != null && real != initiator) return real;

        if (dummyPrefab == null || initiator == null) return null;

        Vector3 pos = initiator.transform.position + initiator.transform.forward * dummyDistance;
        Quaternion rot = Quaternion.LookRotation(-initiator.transform.forward); // face the initiator

        GameObject go = Object.Instantiate(dummyPrefab, pos, rot);
        dummy = go.GetComponent<DummyPartner>();
        if (dummy == null) dummy = go.AddComponent<DummyPartner>();

        return go.GetComponent<PlayerController>();
    }
}
