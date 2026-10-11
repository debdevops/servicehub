namespace ServiceHub.Core.Helpers;

/// <summary>
/// Bounds how many different error shapes one queue and reason may split into (design 10 §5). Without a bound, messages
/// whose text carries unpredictable words would each become their own signature, and none would ever earn trust.
/// </summary>
/// <remarks>
/// The first <see cref="DefaultMax"/> shapes seen keep their own signature, in the order they were first seen; every later new
/// shape folds into <see cref="Other"/>. Because membership goes by first sight, a later shape can never displace an earlier one,
/// so a message's signature does not change once it has one. <see cref="Other"/> is by definition a mixture: nothing may trust it
/// above L3 (enforced where trust is read, not here). An empty shape (no error text) is its own group and does not use a slot.
/// </remarks>
public static class ErrorTemplateCap
{
    /// <summary>The proposed limit (design 10 O4). Tune after real traffic.</summary>
    public const int DefaultMax = 20;

    /// <summary>The shape every folded message gets.</summary>
    public const string Other = "<other>";

    /// <summary>
    /// Returns <paramref name="template"/> if it is already one of the <paramref name="known"/> shapes or there is still a free
    /// slot; otherwise <see cref="Other"/>. <paramref name="known"/> holds the shapes in use so far (without the empty one or
    /// <see cref="Other"/>).
    /// </summary>
    public static string Apply(IReadOnlyCollection<string> known, string template, int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        if (template.Length == 0 || template == Other)
        {
            return template;
        }

        if (known.Contains(template))
        {
            return template;
        }

        return known.Count(t => t.Length > 0 && t != Other) < max ? template : Other;
    }

    /// <summary>
    /// Applies the cap to a whole queue's shapes at once. <paramref name="templatesOldestFirst"/> are the shapes of its messages in
    /// the order they were detected; the result is the same length and gives each message what <see cref="Apply"/> would have
    /// given it when it arrived — the first <paramref name="max"/> distinct shapes keep their own, every other new shape is
    /// <see cref="Other"/>.
    /// </summary>
    public static IReadOnlyList<string> AssignAll(IReadOnlyList<string> templatesOldestFirst, int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(templatesOldestFirst);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        var known = new HashSet<string>(StringComparer.Ordinal);
        var result = new string[templatesOldestFirst.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var t = templatesOldestFirst[i];
            if (t.Length == 0 || t == Other)
            {
                result[i] = t;
            }
            else if (known.Contains(t))
            {
                result[i] = t;
            }
            else if (known.Count < max)
            {
                known.Add(t);
                result[i] = t;
            }
            else
            {
                result[i] = Other;
            }
        }

        return result;
    }
}
