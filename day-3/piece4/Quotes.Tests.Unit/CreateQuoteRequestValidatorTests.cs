using FluentAssertions;
using Quotes.Domain;

namespace Quotes.Tests.Unit;

public sealed class CreateQuoteRequestValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankAuthor_ReturnsAuthorRequired(string? author)
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest(author, "This quote is valid.", ["api"]);

        var result = validator.Validate(request);

        result.Errors.Should().ContainSingle(error =>
            error.Field == nameof(CreateQuoteRequest.Author)
            && error.Message == "Author is required.");
    }

    [Fact]
    public void Validate_AuthorLongerThanMaximum_ReturnsAuthorLengthError()
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest(new string('a', 81), "This quote is valid.", ["api"]);

        var result = validator.Validate(request);

        result.Errors.Should().ContainSingle(error =>
            error.Field == nameof(CreateQuoteRequest.Author)
            && error.Message == "Author cannot exceed 80 characters.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankText_ReturnsTextRequired(string? text)
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("Shagun", text, ["api"]);

        var result = validator.Validate(request);

        result.Errors.Should().ContainSingle(error =>
            error.Field == nameof(CreateQuoteRequest.Text)
            && error.Message == "Text is required.");
    }

    [Fact]
    public void Validate_TextShorterThanMinimum_ReturnsTextMinimumError()
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("Shagun", "too short", ["api"]);

        var result = validator.Validate(request);

        result.Errors.Should().ContainSingle(error =>
            error.Field == nameof(CreateQuoteRequest.Text)
            && error.Message == "Text must be at least 10 characters.");
    }

    [Fact]
    public void Validate_TextLongerThanMaximum_ReturnsTextMaximumError()
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("Shagun", new string('q', 281), ["api"]);

        var result = validator.Validate(request);

        result.Errors.Should().ContainSingle(error =>
            error.Field == nameof(CreateQuoteRequest.Text)
            && error.Message == "Text cannot exceed 280 characters.");
    }

    [Fact]
    public void Validate_TooManyTags_ReturnsTagCountError()
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("Shagun", "This quote is valid.", ["a", "b", "c", "d", "e", "f"]);

        var result = validator.Validate(request);

        result.Errors.Should().ContainSingle(error =>
            error.Field == nameof(CreateQuoteRequest.Tags)
            && error.Message == "A quote cannot have more than 5 tags.");
    }

    [Fact]
    public void Validate_BlankTag_ReturnsBlankTagError()
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("Shagun", "This quote is valid.", ["api", " "]);

        var result = validator.Validate(request);

        result.Errors.Should().ContainSingle(error =>
            error.Field == nameof(CreateQuoteRequest.Tags)
            && error.Message == "Tags cannot be blank.");
    }

    [Fact]
    public void Validate_ValidRequest_ReturnsValidResult()
    {
        var validator = new CreateQuoteRequestValidator();
        var request = new CreateQuoteRequest("Shagun", "This quote is valid.", ["api"]);

        var result = validator.Validate(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
