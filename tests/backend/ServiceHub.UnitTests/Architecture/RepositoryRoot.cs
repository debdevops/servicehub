namespace ServiceHub.UnitTests.Architecture;

/// <summary>
/// Finds the repository root from the test binary's location, so the source-scanning architecture
/// tests work from any working directory (IDE, <c>dotnet test</c>, CI).
/// </summary>
internal static class RepositoryRoot
{
    private static readonly Lazy<DirectoryInfo> Root = new(Find);

    /// <summary>The repository root directory.</summary>
    public static DirectoryInfo Directory => Root.Value;

    /// <summary>Every C# source file under a path relative to the repository root.</summary>
    public static IEnumerable<FileInfo> CSharpFilesUnder(string relativePath)
    {
        var dir = new DirectoryInfo(Path.Combine(Directory.FullName, relativePath));
        if (!dir.Exists)
        {
            return [];
        }

        return dir
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static DirectoryInfo Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            // The root is the one holding both the archive and the new stack.
            if (System.IO.Directory.Exists(Path.Combine(dir.FullName, "archive"))
                && System.IO.Directory.Exists(Path.Combine(dir.FullName, "services")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'. " +
            "The architecture tests scan source files and need it.");
    }
}
