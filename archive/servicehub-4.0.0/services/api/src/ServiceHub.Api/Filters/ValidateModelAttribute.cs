using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ServiceHub.Api.Filters;

/// <summary>
/// Filter attribute that automatically validates model state and returns 400 Bad Request
/// with detailed validation errors when the model is invalid.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ValidateModelAttribute : ActionFilterAttribute
{
    /// <inheritdoc/>
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
        {
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown";

            var problemDetails = new ValidationProblemDetails(context.ModelState)
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest,
                Instance = context.HttpContext.Request.Path,
                Extensions =
                {
                    ["correlationId"] = correlationId,
                    ["traceId"] = context.HttpContext.TraceIdentifier
                }
            };

            context.Result = new BadRequestObjectResult(problemDetails);
        }

        base.OnActionExecuting(context);
    }
}
