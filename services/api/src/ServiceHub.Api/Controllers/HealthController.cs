using Microsoft.AspNetCore.Mvc;

namespace ServiceHub.Api.Controllers;

/// <summary>
/// Controller for health check endpoints with detailed information.
/// Provides programmatic health check endpoints beyond the standard ASP.NET Core health checks.
/// </summary>
[ApiController]
[Route("api/health")]
[Tags("Health")]
public sealed class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthController"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the API version information.
    /// </summary>
    /// <returns>The API version information.</returns>
    /// <response code="200">Version information retrieved successfully.</response>
    [HttpGet("version")]
    [ProducesResponseType(typeof(VersionInfo), StatusCodes.Status200OK)]
    public IActionResult GetVersion()
    {
        var assembly = typeof(HealthController).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        var informationalVersion = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? version;

        var versionInfo = new VersionInfo(
            Version: version,
            InformationalVersion: informationalVersion,
            Environment: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            MachineName: Environment.MachineName,
            OsDescription: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            FrameworkDescription: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            StartedAt: ProcessStartTime);

        return Ok(versionInfo);
    }

    /// <summary>
    /// Gets detailed status of the API and its dependencies.
    /// </summary>
    /// <returns>The detailed status information.</returns>
    /// <response code="200">Status information retrieved successfully.</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(StatusInfo), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();

        var statusInfo = new StatusInfo(
            IsHealthy: true,
            Uptime: DateTimeOffset.UtcNow - ProcessStartTime,
            MemoryUsageMb: process.WorkingSet64 / (1024 * 1024),
            ThreadCount: process.Threads.Count,
            GcTotalMemoryMb: GC.GetTotalMemory(false) / (1024 * 1024),
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            Timestamp: DateTimeOffset.UtcNow);

        return Ok(statusInfo);
    }

    /// <summary>
    /// Gets the process start time (cached).
    /// </summary>
    private static readonly DateTimeOffset ProcessStartTime = DateTimeOffset.UtcNow;
}

/// <summary>
/// API version information.
/// </summary>
/// <param name="Version">The assembly version.</param>
/// <param name="InformationalVersion">The informational version with commit hash.</param>
/// <param name="Environment">The hosting environment.</param>
/// <param name="MachineName">The machine name.</param>
/// <param name="OsDescription">The OS description.</param>
/// <param name="FrameworkDescription">The .NET framework description.</param>
/// <param name="StartedAt">When the application started.</param>
public sealed record VersionInfo(
    string Version,
    string InformationalVersion,
    string Environment,
    string MachineName,
    string OsDescription,
    string FrameworkDescription,
    DateTimeOffset StartedAt);

/// <summary>
/// Detailed status information.
/// </summary>
/// <param name="IsHealthy">Whether the API is healthy.</param>
/// <param name="Uptime">The application uptime.</param>
/// <param name="MemoryUsageMb">Memory usage in megabytes.</param>
/// <param name="ThreadCount">The thread count.</param>
/// <param name="GcTotalMemoryMb">Total GC memory in megabytes.</param>
/// <param name="Gen0Collections">Gen 0 collection count.</param>
/// <param name="Gen1Collections">Gen 1 collection count.</param>
/// <param name="Gen2Collections">Gen 2 collection count.</param>
/// <param name="Timestamp">When the status was retrieved.</param>
public sealed record StatusInfo(
    bool IsHealthy,
    TimeSpan Uptime,
    long MemoryUsageMb,
    int ThreadCount,
    long GcTotalMemoryMb,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    DateTimeOffset Timestamp);
