using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionMatchMagnitude;
using Jobbliggaren.Application.CompanyWatches.Queries.PreviewCriterionMatchMagnitude;
using Jobbliggaren.Domain.CompanyWatches;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>
/// #560 PR-3 (Fork G3, second consumer) — the picker's live preview of an UNSAVED criterion.
/// No persistence involved; the Domain spec is the gate and the ceiling is the same single-sourced
/// constant the saved-criterion path uses (the preview and the eventual headline can never
/// disagree by construction).
/// </summary>
public class PreviewCriterionMatchMagnitudeQueryHandlerTests
{
    private static readonly string[] SniIt = ["62100"];
    private static readonly string[] KommunStockholm = ["0180"];

    private static PreviewCriterionMatchMagnitudeQueryHandler HandlerFor(
        ICompanyWatchBrowseQuery port, Guid? userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return new PreviewCriterionMatchMagnitudeQueryHandler(currentUser, port);
    }

    [Fact]
    public async Task Handle_ValidUnsavedInput_CountsWithTheSingleSourcedCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyWatchBrowseQuery>();
        port.CountMatchingCompaniesAsync(
                Arg.Any<CompanyWatchCriteriaSpec>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(412);

        var result = await HandlerFor(port, Guid.NewGuid()).Handle(
            new PreviewCriterionMatchMagnitudeQuery(
                new CompanyWatchCriteriaInput(["62100"], ["0180"])),
            ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Magnitude.ShouldBe(412);
        result.Value.Saturated.ShouldBeFalse();

        await port.Received(1).CountMatchingCompaniesAsync(
            Arg.Is<CompanyWatchCriteriaSpec>(s =>
                s.SniCodes.SequenceEqual(SniIt)
                && s.MunicipalityCodes.SequenceEqual(KommunStockholm)),
            CriterionMatchMagnitudeDto.Ceiling,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidInput_FailsWithTheDomainError_WithoutTouchingTheRegister()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var result = await HandlerFor(port, Guid.NewGuid()).Handle(
            new PreviewCriterionMatchMagnitudeQuery(new CompanyWatchCriteriaInput(["62100"], [])),
            ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.MunicipalityRequired");
        await port.DidNotReceiveWithAnyArgs()
            .CountMatchingCompaniesAsync(default!, default, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_NoAuthenticatedUser_FailsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var port = Substitute.For<ICompanyWatchBrowseQuery>();

        var result = await HandlerFor(port, userId: null).Handle(
            new PreviewCriterionMatchMagnitudeQuery(
                new CompanyWatchCriteriaInput(["62100"], ["0180"])),
            ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriterion.Unauthorized");
        await port.DidNotReceiveWithAnyArgs()
            .CountMatchingCompaniesAsync(default!, default, CancellationToken.None);
    }
}
