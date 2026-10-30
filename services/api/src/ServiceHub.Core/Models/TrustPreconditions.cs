using System.Security.Cryptography;
using System.Text;
using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Models;

/// <summary>
/// What must still be true for earned trust to apply (plan G2): a fingerprint of the settings ServiceHub itself keeps about the
/// connection a signature belongs to. If it changes, the fix returns to approval.
/// </summary>
/// <remarks>
/// Only what ServiceHub stores is fingerprinted: the cloud, the environment and whether the connection may send. Settings that
/// live in the cloud (maximum deliveries, lock duration, time to live) are not stored, so a change there cannot be seen.
/// </remarks>
public static class TrustPreconditions
{
    /// <summary>The fingerprint of a connection's settings that trust depends on.</summary>
    public static string Fingerprint(Namespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);
        var text = $"provider={ns.Provider};environment={ns.Environment};canSend={ns.CanSend != false}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
