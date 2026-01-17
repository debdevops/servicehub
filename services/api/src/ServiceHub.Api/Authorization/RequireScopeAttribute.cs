namespace ServiceHub.Api.Authorization;

/// <summary>
/// Attribute to specify required API key scope for a controller or action.
/// Use this to enforce granular permissions at the endpoint level.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireScopeAttribute : Attribute
{
    /// <summary>
    /// Gets the required scope for this endpoint.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequireScopeAttribute"/> class.
    /// </summary>
    /// <param name="scope">The required API key scope.</param>
    public RequireScopeAttribute(string scope)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }
}
