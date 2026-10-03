using System.Security.Cryptography;
using System.Text;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Recognises an API key, so an automation caller is <i>named</i> in the audit trail
/// (<c>ApiKey:ops-bot</c>) instead of passing as a browser session.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity, not enforcement.</b> This middleware attributes; it does not gate. A request with
/// no key is a browser session, exactly as before. A request presenting a key that is not
/// configured is refused with a 401 — a wrong credential is never quietly downgraded to
/// anonymous. Scope enforcement is not part of this unit (rule R12).
/// </para>
/// <para>
/// Keys come from <c>Security:Authentication:ApiKeys</c>: each entry is a string, or
/// <c>{ "Key": …, "Description": … }</c> where the description becomes the key's name. Placeholder
/// values are ignored. With no keys configured this middleware does nothing at all.
/// </para>
/// </remarks>
public sealed class ApiKeyIdentityMiddleware
{
    /// <summary>The header carrying the key.</summary>
    public const string HeaderName = "X-API-KEY";

    private const string UnnamedKey = "unnamed key";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyIdentityMiddleware> _logger;
    private readonly IReadOnlyList<(byte[] Hash, string Name)> _keys;

    /// <summary>Creates the middleware from configuration.</summary>
    public ApiKeyIdentityMiddleware(
        RequestDelegate next, ILogger<ApiKeyIdentityMiddleware> logger, IConfiguration configuration)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        var keys = new List<(byte[], string)>();
        foreach (var child in configuration.GetSection("Security:Authentication:ApiKeys").GetChildren())
        {
            var (key, name) = child.GetChildren().Any()
                ? (child["Key"], child["Description"])
                : (child.Value, null);

            if (!string.IsNullOrWhiteSpace(key) && !IsPlaceholder(key))
            {
                keys.Add((Hash(key), string.IsNullOrWhiteSpace(name) ? UnnamedKey : name.Trim()));
            }
        }

        _keys = keys;
        if (_keys.Count > 0)
        {
            _logger.LogInformation("{Count} API key(s) configured for attribution", _keys.Count);
        }
    }

    /// <summary>Invokes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var presented = context.Request.Headers[HeaderName].FirstOrDefault();
        if (_keys.Count == 0 || string.IsNullOrEmpty(presented) || context.Items.ContainsKey("OwnerId"))
        {
            await _next(context);
            return;
        }

        var presentedHash = Hash(presented);
        var match = _keys.FirstOrDefault(k => CryptographicOperations.FixedTimeEquals(k.Hash, presentedHash));
        if (match.Name is null)
        {
            _logger.LogWarning("A request presented an API key that is not configured");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Results.Problem(
                title: "The API key is not recognised",
                detail: "The X-API-KEY header did not match a configured key. Remove it to continue as a browser session.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ErrorCodes.InvalidApiKey,
                    ["correlationId"] = context.TraceIdentifier,
                }).ExecuteAsync(context);
            return;
        }

        // One owner, as ever: a key names who acted, not whose data it is (that is a Users concern).
        context.Items["OwnerId"] = Namespace.SpaOwnerId;
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "ApiKey";
        context.Items["ApiKeyName"] = match.Name;

        await _next(context);
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static bool IsPlaceholder(string key) =>
        key.Contains("CHANGE_THIS", StringComparison.OrdinalIgnoreCase)
        || key.Contains("SET_VIA", StringComparison.OrdinalIgnoreCase);
}
