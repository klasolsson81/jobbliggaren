using Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Feedback.Commands.SubmitFeedback;

/// <summary>
/// #1979 — shape only: a key, a page key and a client must be present. Whether the page is one of the fixed set, and
/// whether a rating or a text was given, is the domain's answer (the handler tests pin those refusals).
/// </summary>
public sealed class SubmitFeedbackCommandValidatorTests
{
    private readonly SubmitFeedbackCommandValidator _validator = new();

    private static SubmitFeedbackCommand Command(Guid key, string? page, ReportedClient client) =>
        new(key, page, Rating: 4, Comment: null, client, AppVersion: null);

    [Fact]
    public void Validate_AKeyAPageAndAClient_Passes() =>
        _validator.Validate(Command(Guid.NewGuid(), "jobs", ReportedClient.None)).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_AnEmptySubmissionKey_Fails()
    {
        var result = _validator.Validate(Command(Guid.Empty, "jobs", ReportedClient.None));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SubmitFeedbackCommand.SubmissionKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_WithoutAPageKey_Fails(string? page)
    {
        var result = _validator.Validate(Command(Guid.NewGuid(), page, ReportedClient.None));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SubmitFeedbackCommand.PageKey));
    }

    [Fact]
    public void Validate_WithoutAClient_Fails()
    {
        var result = _validator.Validate(Command(Guid.NewGuid(), "jobs", null!));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SubmitFeedbackCommand.Client));
    }

    [Fact]
    public void Validate_NeitherARatingNorAText_LeavesTheRefusalToTheDomain() =>
        _validator.Validate(new SubmitFeedbackCommand(Guid.NewGuid(), "jobs", null, null, ReportedClient.None, null))
            .IsValid.ShouldBeTrue();
}
