using ServiceHub.Core.Constants;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// The stable <c>code</c> for a ProblemDetails the framework produced on its own — a 404 from the
/// router, a 400 from model binding — so that no failure leaves without one (ARCHITECTURE §5.2).
/// </summary>
/// <remarks>
/// Only applied when nothing more specific was set: a controller's own code always wins. A status
/// with no honest match maps to <see cref="ErrorCodes.UnexpectedFailure"/> rather than to a code
/// invented here; codes are added when a screen needs to tell failures apart (rule R12).
/// </remarks>
public static class ProblemCodes
{
    /// <summary>The code for a response status.</summary>
    public static string ForStatus(int? status) => status switch
    {
        StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => ErrorCodes.ValidationFailed,
        StatusCodes.Status403Forbidden => ErrorCodes.PermissionDenied,
        StatusCodes.Status404NotFound => ErrorCodes.NotFound,
        _ => ErrorCodes.UnexpectedFailure
    };
}
