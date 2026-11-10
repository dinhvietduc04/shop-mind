using System.Security.Cryptography;
using System.Text;

namespace Shop.Domain;

/// <summary>
/// B5: provider webhook ids are free-form strings; our ledger keys are Guids.
/// Deterministic derivation (MD5 of the id) so the same provider event always
/// maps to the same dedupe/delivery key across intake replays.
/// </summary>
public static class ProviderEventIds
{
    public static Guid ToGuid(string providerEventId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(providerEventId))[..16]);
}
