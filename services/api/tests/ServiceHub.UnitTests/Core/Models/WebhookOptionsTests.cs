using FluentAssertions;
using ServiceHub.Core.Models;

namespace ServiceHub.UnitTests.Core.Models;

public sealed class WebhookOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new WebhookOptions();

        opts.Enabled.Should().BeFalse();
        opts.Url.Should().BeEmpty();
        opts.DlqSpikeThreshold.Should().Be(10);
        opts.CooldownSeconds.Should().Be(300);
    }

    [Fact]
    public void SectionName_IsWebhooks()
    {
        WebhookOptions.SectionName.Should().Be("Webhooks");
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var opts = new WebhookOptions
        {
            Enabled = true,
            Url = "https://hooks.example.com/dlq",
            DlqSpikeThreshold = 25,
            CooldownSeconds = 120
        };

        opts.Enabled.Should().BeTrue();
        opts.Url.Should().Be("https://hooks.example.com/dlq");
        opts.DlqSpikeThreshold.Should().Be(25);
        opts.CooldownSeconds.Should().Be(120);
    }
}
