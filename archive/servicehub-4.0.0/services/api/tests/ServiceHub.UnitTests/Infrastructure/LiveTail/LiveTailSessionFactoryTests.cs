using FluentAssertions;
using Moq;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.LiveTail;

namespace ServiceHub.UnitTests.Infrastructure.LiveTail;

public sealed class LiveTailSessionFactoryTests
{
    [Fact]
    public void Constructor_NullMessageOperationsService_Throws()
    {
        var act = () => new LiveTailSessionFactory(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageOperationsService");
    }

    [Fact]
    public void Create_ReturnsNonNullSession()
    {
        var sut = new LiveTailSessionFactory(Mock.Of<IMessageOperationsService>());

        var session = sut.Create(Guid.NewGuid(), "orders", null, false, CloudProviderType.Azure);

        session.Should().NotBeNull();
        session.Should().BeAssignableTo<ILiveTailSession>();
    }
}
