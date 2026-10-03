using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class MeControllerTests
{
    private static MeController CreateController(
        string? ownerId = null, string? authMethod = null, GovernanceRole? governanceRole = GovernanceRole.Admin)
    {
        var httpContext = new DefaultHttpContext();
        if (ownerId is not null)
        {
            httpContext.Items["OwnerId"] = ownerId;
        }
        if (authMethod is not null)
        {
            httpContext.Items["AuthMethod"] = authMethod;
        }

        var governanceAccessEvaluator = new Mock<IGovernanceAccessEvaluator>();
        governanceAccessEvaluator
            .Setup(e => e.GetEffectiveRoleAsync(
                It.IsAny<string>(), It.IsAny<string>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(governanceRole);

        return new MeController(governanceAccessEvaluator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task Get_WithOidcIdentity_ReturnsOwnerIdAndAuthMethod()
    {
        var controller = CreateController(ownerId: "oidc:user-abc", authMethod: "Oidc");

        var result = await controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.OwnerId.Should().Be("oidc:user-abc");
        response.AuthMethod.Should().Be("Oidc");
    }

    [Fact]
    public async Task Get_NoAuthMethodSet_ReturnsNullAuthMethod()
    {
        var controller = CreateController(ownerId: "some-owner");

        var result = await controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.AuthMethod.Should().BeNull();
    }

    [Fact]
    public async Task Get_NoOwnerIdSet_DefaultsToSpaOwner()
    {
        var controller = CreateController();

        var result = await controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.OwnerId.Should().Be(Namespace.SpaOwnerId);
    }

    [Fact]
    public async Task Get_IncludesEffectiveGovernanceRole()
    {
        var controller = CreateController(ownerId: "some-owner", governanceRole: GovernanceRole.Operator);

        var result = await controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.GovernanceRole.Should().Be("Operator");
    }

    [Fact]
    public async Task Get_NoGovernanceGrantForCaller_ReturnsNullRole()
    {
        var controller = CreateController(ownerId: "some-owner", governanceRole: null);

        var result = await controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.GovernanceRole.Should().BeNull();
    }
}
