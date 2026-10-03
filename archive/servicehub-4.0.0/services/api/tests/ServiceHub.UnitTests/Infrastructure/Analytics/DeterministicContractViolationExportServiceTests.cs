using FluentAssertions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Analytics;

namespace ServiceHub.UnitTests.Infrastructure.Analytics;

public sealed class DeterministicContractViolationExportServiceTests
{
    private readonly DeterministicContractViolationExportService _sut = new();

    private static Namespace CreateNamespace(string name = "orders-ns") =>
        Namespace.Create(
            name,
            $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS").Value;

    [Fact]
    public void BuildReport_NullNamespace_Throws()
    {
        var act = () => _sut.BuildReport(null!, [], DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildReport_NullFindings_Throws()
    {
        var act = () => _sut.BuildReport(CreateNamespace(), null!, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildReport_NoFindings_ReturnsEmptyReportNotingNoViolations()
    {
        var ns = CreateNamespace();
        var start = DateTimeOffset.UtcNow.AddHours(-24);
        var end = DateTimeOffset.UtcNow;

        var report = _sut.BuildReport(ns, [], start, end);

        report.NamespaceId.Should().Be(ns.Id);
        report.NamespaceName.Should().Be(ns.Name);
        report.Violations.Should().BeEmpty();
        report.MarkdownReport.Should().Contain("No contract violations were detected");
    }

    [Fact]
    public void BuildReport_SchemaShapeDrift_TranslatesToPlainEnglishViolationType()
    {
        var ns = CreateNamespace();
        var finding = DriftFinding.Create(
            ns.Id,
            "orders-queue",
            DriftFindingType.SchemaShapeDrift,
            85,
            "Schema drift detected",
            recommendedActions: ["Review recent producer deployments"]);

        var report = _sut.BuildReport(ns, [finding], DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        report.Violations.Should().ContainSingle();
        var violation = report.Violations[0];
        violation.EntityName.Should().Be("orders-queue");
        violation.ViolationType.Should().Be("Message field shape changed");
        violation.Priority.Should().Be("High");
        violation.Evidence.Should().Be(finding.Description);
        violation.SuggestedFixes.Should().Contain("Review recent producer deployments");
    }

    [Fact]
    public void BuildReport_PayloadFormatDrift_TranslatesToPlainEnglishViolationType()
    {
        var ns = CreateNamespace();
        var finding = DriftFinding.Create(
            ns.Id, "orders-queue", DriftFindingType.PayloadFormatDrift, 55, "Payload format drift detected");

        var report = _sut.BuildReport(ns, [finding], DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        report.Violations[0].ViolationType.Should().Be("Message payload format changed");
        report.Violations[0].Priority.Should().Be("Medium");
    }

    [Theory]
    [InlineData(100, "High")]
    [InlineData(70, "High")]
    [InlineData(69, "Medium")]
    [InlineData(40, "Medium")]
    [InlineData(39, "Low")]
    [InlineData(0, "Low")]
    public void BuildReport_SeverityBands_MapToExpectedPriority(int severity, string expectedPriority)
    {
        var ns = CreateNamespace();
        var finding = DriftFinding.Create(ns.Id, "orders-queue", DriftFindingType.SchemaShapeDrift, severity, "desc");

        var report = _sut.BuildReport(ns, [finding], DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        report.Violations[0].Priority.Should().Be(expectedPriority);
    }

    [Fact]
    public void BuildReport_MultipleFindings_OrdersBySeverityDescendingThenEntityName()
    {
        var ns = CreateNamespace();
        var low = DriftFinding.Create(ns.Id, "b-queue", DriftFindingType.SchemaShapeDrift, 30, "low sev");
        var highA = DriftFinding.Create(ns.Id, "a-queue", DriftFindingType.SchemaShapeDrift, 90, "high sev a");
        var highB = DriftFinding.Create(ns.Id, "z-queue", DriftFindingType.SchemaShapeDrift, 90, "high sev z");

        var report = _sut.BuildReport(ns, [low, highB, highA], DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        report.Violations.Select(v => v.EntityName).Should().ContainInOrder("a-queue", "z-queue", "b-queue");
    }

    [Fact]
    public void BuildReport_MarkdownReport_ContainsEntityAndSuggestedFix()
    {
        var ns = CreateNamespace("payments-ns");
        var finding = DriftFinding.Create(
            ns.Id,
            "payments-queue",
            DriftFindingType.SchemaShapeDrift,
            80,
            "Field shape changed",
            recommendedActions: ["Confirm with the producer team"]);

        var report = _sut.BuildReport(ns, [finding], DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow);

        report.MarkdownReport.Should().Contain("payments-ns");
        report.MarkdownReport.Should().Contain("payments-queue");
        report.MarkdownReport.Should().Contain("High priority");
        report.MarkdownReport.Should().Contain("Confirm with the producer team");
    }
}
