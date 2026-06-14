using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.DeriveOccupationCodes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.DeriveOccupationCodes;

// Fas 4 STEG 3 (F4-3, ADR 0040 amendment + ADR 0074) — DeriveOccupationCodesQueryHandler
// är en TUNN adapter mot IOccupationCodeDeriver-porten (speglar
// TaxonomyQueryHandlersTests / SuggestJobAdTermsQueryHandler). Ingen
// matchnings-logik i handlern; porten mockas med NSubstitute. Verifierar ren
// delegering + DTO-passthrough + CancellationToken-propagering (CLAUDE.md §3
// async end-to-end). Den RIKTIGA derivations-logiken testas mot riktig taxonomi
// + frusen map i Api.IntegrationTests/OccupationDerivation (samma split som F4-2).
//
// RED tills IOccupationCodeDeriver + OccupationDerivationResult/OccupationCandidate/
// OccupationMatchKind + DeriveOccupationCodesQuery(+Handler) finns.
//
// CA2012: NSubstitute-stubbning av ValueTask-returnerande port-medlemmar är ett
// känt analyzer-false-positive (substitute-anropet KONSUMERAS aldrig — det
// interceptas av NSubstitute för att registrera Returns). Suppression scoped
// till mock-setup, ej produktionskod. Speglar TaxonomyQueryHandlersTests.
#pragma warning disable CA2012
public class DeriveOccupationCodesQueryHandlerTests
{
    private readonly IOccupationCodeDeriver _deriver =
        Substitute.For<IOccupationCodeDeriver>();

    [Fact]
    public async Task Handle_ShouldReturnPortResult_WhenDeriveOccupationCodesQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new OccupationDerivationResult(
            "Advokat",
            [
                new OccupationCandidate(
                    "q8wL_kdi_WaW", "Advokater",
                    OccupationMatchKind.ExactOccupationName, "Advokat"),
            ]);
        _deriver.DeriveAsync("Advokat", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(expected));
        var sut = new DeriveOccupationCodesQueryHandler(_deriver);

        var result = await sut.Handle(new DeriveOccupationCodesQuery("Advokat"), ct);

        // Tunn adapter — exakt samma instans tillbaka (ingen om-projektion).
        result.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Handle_ShouldDelegateOnceToPort_WhenDeriveOccupationCodesQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        _deriver.DeriveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(
                new OccupationDerivationResult("Systemutvecklare", [])));
        var sut = new DeriveOccupationCodesQueryHandler(_deriver);

        await sut.Handle(new DeriveOccupationCodesQuery("Systemutvecklare"), ct);

        await _deriver.Received(1).DeriveAsync(
            "Systemutvecklare", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPassTitleThroughToPort_WhenDeriveOccupationCodesQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        _deriver.DeriveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(
                new OccupationDerivationResult("Förskollärare", [])));
        var sut = new DeriveOccupationCodesQueryHandler(_deriver);

        await sut.Handle(new DeriveOccupationCodesQuery("Förskollärare"), ct);

        // Titeln måste nå porten oförändrad (handlern transformerar inte input).
        await _deriver.Received(1).DeriveAsync(
            "Förskollärare", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldPropagateCancellationToken_WhenDeriveOccupationCodesQuery()
    {
        // CLAUDE.md §3 — CancellationToken propageras end-to-end. Handlern får
        // INTE svälja eller byta token mot default; samma token ska nå porten.
        using var cts = new CancellationTokenSource();
        _deriver.DeriveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(
                new OccupationDerivationResult("Advokat", [])));
        var sut = new DeriveOccupationCodesQueryHandler(_deriver);

        await sut.Handle(new DeriveOccupationCodesQuery("Advokat"), cts.Token);

        await _deriver.Received(1).DeriveAsync("Advokat", cts.Token);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyCandidates_WhenPortFindsNoMatch()
    {
        // No-match-kontraktet (ADR 0040 Beslut 4): tom kandidat-lista, ALDRIG
        // throw/auto-select. Handlern returnerar porten ord-för-ord — verifierar
        // att tom-fallet propageras rent (UX faller till manuellt val).
        var ct = TestContext.Current.CancellationToken;
        var empty = new OccupationDerivationResult("xyzzy qwerty", []);
        _deriver.DeriveAsync("xyzzy qwerty", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<OccupationDerivationResult>(empty));
        var sut = new DeriveOccupationCodesQueryHandler(_deriver);

        var result = await sut.Handle(
            new DeriveOccupationCodesQuery("xyzzy qwerty"), ct);

        result.ShouldBeSameAs(empty);
        result.Candidates.ShouldBeEmpty();
    }
}
#pragma warning restore CA2012
