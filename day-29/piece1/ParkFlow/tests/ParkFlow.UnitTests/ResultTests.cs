using ParkFlow.BuildingBlocks.Application;

namespace ParkFlow.UnitTests;

/// <summary>
/// Day 29 (see day-28/piece1/BUILD-PLAN.md): proves Result's new error-kind shape only — no
/// controller or application-service call site changes yet, so these tests don't touch HTTP.
/// </summary>
public class ResultTests
{
    [Fact]
    public void Success_HasNoErrorKind()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Null(result.ErrorKind);
    }

    [Fact]
    public void Failure_WithNoErrorKindSpecified_DefaultsToValidation()
    {
        var result = Result.Failure("bad input");

        Assert.False(result.IsSuccess);
        Assert.Equal("bad input", result.Error);
        Assert.Equal(ResultErrorKind.Validation, result.ErrorKind);
    }

    [Fact]
    public void NotFound_HasNotFoundErrorKind()
    {
        var result = Result.NotFound("no reservation with that id");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public void Forbidden_HasForbiddenErrorKind()
    {
        var result = Result.Forbidden("not your reservation");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    [Fact]
    public void GenericSuccess_CarriesValue_AndNoErrorKind()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.ErrorKind);
    }

    [Fact]
    public void GenericFailure_WithNoErrorKindSpecified_DefaultsToValidation()
    {
        var result = Result.Failure<Guid>("bad input");

        Assert.False(result.IsSuccess);
        Assert.Equal(default, result.Value);
        Assert.Equal(ResultErrorKind.Validation, result.ErrorKind);
    }

    [Fact]
    public void GenericNotFound_HasDefaultValue_AndNotFoundErrorKind()
    {
        var result = Result.NotFound<Guid>("no reservation with that id");

        Assert.False(result.IsSuccess);
        Assert.Equal(default, result.Value);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public void GenericForbidden_HasForbiddenErrorKind()
    {
        var result = Result.Forbidden<Guid>("not your reservation");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }
}
