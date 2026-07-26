using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class MeControllerTests
{
    private static MeController CreateController(string? ownerId = null, string? authMethod = null)
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

        return new MeController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public void Get_WithOidcIdentity_ReturnsOwnerIdAndAuthMethod()
    {
        var controller = CreateController(ownerId: "oidc:user-abc", authMethod: "Oidc");

        var result = controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.OwnerId.Should().Be("oidc:user-abc");
        response.AuthMethod.Should().Be("Oidc");
    }

    [Fact]
    public void Get_NoAuthMethodSet_ReturnsNullAuthMethod()
    {
        var controller = CreateController(ownerId: "some-owner");

        var result = controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.AuthMethod.Should().BeNull();
    }

    [Fact]
    public void Get_NoOwnerIdSet_DefaultsToSpaOwner()
    {
        var controller = CreateController();

        var result = controller.Get();

        var response = ((OkObjectResult)result.Result!).Value.Should().BeOfType<MeResponse>().Subject;
        response.OwnerId.Should().Be(Namespace.SpaOwnerId);
    }
}
