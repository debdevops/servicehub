using FluentAssertions;

namespace ServiceHub.UnitTests.Api.Configuration;

public class SecurityConfigurationTests
{

    [Theory]
    [InlineData("appsettings.Production.json")]
    [InlineData("appsettings.json")]
    public void AppsettingsFiles_MustNotContainHardcodedSecrets(string filename)
    {
        var filePath = Path.Combine(GetApiProjectRoot(), filename);

        File.Exists(filePath).Should().BeTrue($"Appsettings file should exist at {filePath}");

        var content = File.ReadAllText(filePath);

        // Check for hardcoded secrets using text-based patterns (more reliable than JSON parsing)
        var secrets = FindHardcodedSecrets(content);
        secrets.Should().BeEmpty(
            "Appsettings files must not contain hardcoded secrets. " +
            "Use environment variables (SET_VIA_ENV_VAR or ${VAR}) for sensitive values. " +
            $"Found suspicious values: {string.Join(", ", secrets)}");
    }

    private static List<string> FindHardcodedSecrets(string content)
    {
        var findings = new List<string>();
        var lines = content.Split('\n');
        int lineNumber = 1;

        foreach (var line in lines)
        {
            // Skip comments and safe lines
            var trimmed = line.Trim();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                continue;

            // Look for patterns that indicate a hardcoded value
            if (trimmed.Contains(":") && !trimmed.Contains("SET_VIA_ENV_VAR") && !trimmed.Contains("${"))
            {
                // Extract the value part (after the colon)
                var colonIndex = trimmed.LastIndexOf(':');
                if (colonIndex > 0 && colonIndex < trimmed.Length - 1)
                {
                    var value = trimmed.Substring(colonIndex + 1).Trim();
                    value = value.TrimEnd(',');

                    // Check for 64+ hex digit patterns (actual encryption keys)
                    if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-f0-9]{64,}$"))
                    {
                        findings.Add($"Line {lineNumber}: {value.Substring(0, Math.Min(20, value.Length))}...");
                    }
                }
            }

            lineNumber++;
        }

        return findings;
    }

    [Fact]
    public void DockerComposeFile_MustNotHaveHardcodedEncryptionKey()
    {
        var filePath = Path.Combine(GetRepoRoot(), "docker-compose.yml");
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);

        // Should NOT contain the old hardcoded key
        var oldKey = "0a1715d2edbbbef62384ee797ed6bf7ce01ef56ee7da55dcc04fb7f297e53a4d";
        content.Should().NotContain(oldKey,
            "Hardcoded encryption key must be removed from docker-compose.yml. " +
            "Use environment variable substitution instead.");

        // Should contain environment variable reference
        content.Should().Contain("SECURITY__ENCRYPTIONKEY",
            "docker-compose.yml must reference SECURITY__ENCRYPTIONKEY environment variable");
    }

    private static string GetApiProjectRoot()
    {
        // Walk up from current directory to find services/api/src/ServiceHub.Api
        var current = Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(current, "services", "api", "src", "ServiceHub.Api")))
        {
            var parent = Directory.GetParent(current);
            if (parent == null)
                throw new InvalidOperationException("Cannot find ServiceHub.Api project root");
            current = parent.FullName;
        }
        return Path.Combine(current, "services", "api", "src", "ServiceHub.Api");
    }

    private static string GetRepoRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(current, "docker-compose.yml")))
        {
            var parent = Directory.GetParent(current);
            if (parent == null)
                throw new InvalidOperationException("Cannot find repo root (docker-compose.yml not found)");
            current = parent.FullName;
        }
        return current;
    }
}
