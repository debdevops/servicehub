using Microsoft.AspNetCore.Mvc.ActionConstraints;

namespace ServiceHub.Api.Filters;

/// <summary>
/// Action constraint that makes an action selectable only when the host is running
/// under <c>ASPNETCORE_ENVIRONMENT=Simulator</c>. In any other environment the
/// decorated actions are not matched during routing, so requests receive a 404 —
/// the endpoints effectively do not bind. This is defense-in-depth alongside the
/// Simulator-only DI registration of the underlying store/clock/seeder services.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SimulatorOnlyAttribute : Attribute, IActionConstraint
{
    private const string SimulatorEnvironmentName = "Simulator";

    /// <summary>
    /// Runs late in action-constraint ordering so it acts as a final gate after
    /// standard HTTP-method/route constraints.
    /// </summary>
    public int Order => 1000;

    /// <inheritdoc />
    public bool Accept(ActionConstraintContext context)
    {
        var environment = context.RouteContext.HttpContext.RequestServices
            .GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;

        // Fail closed: if the environment cannot be resolved, do not expose the action.
        return environment is not null
            && environment.IsEnvironment(SimulatorEnvironmentName);
    }
}
