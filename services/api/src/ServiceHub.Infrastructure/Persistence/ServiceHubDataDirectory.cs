using Microsoft.Extensions.Configuration;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Where ServiceHub keeps its state: the SQLite file, its write-ahead log and the instance lock.
/// One rule, in one place, so the lock, the database and the health check can never disagree.
/// </summary>
public static class ServiceHubDataDirectory
{
    /// <summary>Configuration key. The Dockerfile sets it as <c>ServiceHub__DataDirectory=/data</c>.</summary>
    public const string ConfigurationKey = "ServiceHub:DataDirectory";

    /// <summary>The database file name inside the data directory.</summary>
    public const string DatabaseFileName = "servicehub.db";

    /// <summary>
    /// The configured directory, or <c>./data</c> beneath the current directory when none is set.
    /// A relative value is resolved against the current directory, so the answer is always absolute.
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = configuration[ConfigurationKey];
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured) ? "data" : configured);
    }

    /// <summary>The full path of the database file.</summary>
    public static string ResolveDatabasePath(IConfiguration configuration) =>
        Path.Combine(Resolve(configuration), DatabaseFileName);
}
