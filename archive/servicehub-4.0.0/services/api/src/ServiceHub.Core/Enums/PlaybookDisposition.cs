namespace ServiceHub.Core.Enums;

/// <summary>The terminal human decision on a <see cref="Entities.PlaybookEntry"/> — null while
/// the entry hasn't received one yet (including <c>Expired</c>/<c>Superseded</c>, which are
/// terminal but not human decisions).</summary>
public enum PlaybookDisposition
{
    /// <summary>Accepted, as proposed or after edits.</summary>
    Approved = 0,

    /// <summary>Declined. Requires a reason.</summary>
    Rejected = 1,
}
