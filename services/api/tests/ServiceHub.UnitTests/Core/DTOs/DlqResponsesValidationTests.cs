using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FluentAssertions;
using ServiceHub.Core.DTOs.Responses;

namespace ServiceHub.UnitTests.Core.DTOs;

/// <summary>
/// Regression coverage for the ASP.NET Core record-primary-constructor validation pitfall:
/// putting a validation attribute on the synthesized property (via a `property:` target)
/// instead of the constructor parameter makes ASP.NET Core's model binder silently skip it
/// for record DTOs, even though `Validator.TryValidateObject` (plain reflection over
/// properties) would still find it. MVC's `DefaultModelMetadataProvider` binds and
/// validates records via their primary constructor parameters, so the attribute must be
/// declared on the parameter — see the "has validation metadata defined on property ...
/// that will be ignored" warning this guards against.
/// </summary>
public sealed class DlqResponsesValidationTests
{
    private static ParameterInfo GetPrimaryConstructorParameter(Type recordType, string parameterName)
    {
        var parameter = recordType
            .GetConstructors()
            .Single(c => c.GetParameters().Any(p => p.Name == parameterName))
            .GetParameters()
            .Single(p => p.Name == parameterName);
        return parameter;
    }

    [Fact]
    public void UpdateDlqStatusRequest_Notes_ValidationAttribute_IsOnConstructorParameter()
    {
        var parameter = GetPrimaryConstructorParameter(typeof(UpdateDlqStatusRequest), "Notes");

        parameter.GetCustomAttributes<StringLengthAttribute>().Should().ContainSingle(
            "ASP.NET Core validates records via constructor parameters, so the attribute " +
            "must target the parameter, not the synthesized property, or it is silently ignored");
    }

    [Fact]
    public void UpdateDlqNotesRequest_Notes_ValidationAttribute_IsOnConstructorParameter()
    {
        var parameter = GetPrimaryConstructorParameter(typeof(UpdateDlqNotesRequest), "Notes");

        parameter.GetCustomAttributes<StringLengthAttribute>().Should().ContainSingle(
            "ASP.NET Core validates records via constructor parameters, so the attribute " +
            "must target the parameter, not the synthesized property, or it is silently ignored");
    }

    // Note: plain System.ComponentModel.DataAnnotations.Validator.TryValidateObject reflects
    // over PropertyInfo, not ConstructorInfo parameters, so it cannot observe a parameter-only
    // attribute either way — it is not a substitute for the ASP.NET Core MVC binding pipeline.
    // See DlqHistoryStatusValidationTests (integration) for coverage of the actual HTTP behavior.
}
