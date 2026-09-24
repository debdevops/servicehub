using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Registration for the agent platform. <b>This is the "one registration line"</b> that, with one
/// new file, is the entire cost of adding an agent (PLAN 1.5, Gate 4 ②).
/// </summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Adds the agent registry and the host that runs every registered agent. Call once; call
    /// <see cref="AddAgent{TAgent}"/> for each agent.
    /// </summary>
    public static IServiceCollection AddAgentPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AgentRegistry>();
        services.TryAddSingleton<IAgentRegistry>(sp => sp.GetRequiredService<AgentRegistry>());
        services.AddHostedService<AgentHost>();
        return services;
    }

    /// <summary>
    /// Registers one agent. The Agents screen, its health, its history and its pause control all
    /// follow from this line — <b>no UI, API, database or navigation change is needed, and if one
    /// turns out to be needed, the seam is wrong and gets fixed rather than worked around.</b>
    /// </summary>
    public static IServiceCollection AddAgent<TAgent>(this IServiceCollection services)
        where TAgent : class, IAgent
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAgent, TAgent>();
        return services;
    }
}
