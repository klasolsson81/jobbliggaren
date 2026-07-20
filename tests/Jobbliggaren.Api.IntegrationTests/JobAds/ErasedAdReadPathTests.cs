using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Queries.GetMyMatches;
using Jobbliggaren.Application.SavedJobAds.Commands.SaveJobAd;
using Jobbliggaren.Application.SavedJobAds.Queries.ListSavedJobAds;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #842 — read paths that must NOT surface an erased ad, driven through the REAL handlers on REAL
/// Postgres. <b>THREE of the five guarded paths live here</b> (matches, saved ads, the
/// save-command); the two DIGEST paths are guarded in <c>DigestDispatchJobTests</c>, which has the
/// Worker harness this file does not. An earlier header claimed "every read path" while covering
/// three of five (round-3 Minor 7, carried two rounds) — the split is now written down instead of
/// implied.
/// </summary>
/// <remarks>
/// <b>An erased ad is a TOMBSTONE ROW, not a missing one.</b> <c>JobAd.Erase()</c> blanks the record
/// and leaves it in the table, so an unguarded join gets <c>Title = ""</c> and
/// <c>Company = "[raderad]"</c> — the tombstone's own marker — and renders it. Every comment in the
/// codebase that said "the inner join drops it" was written for the ARCHIVED case and is false for
/// this one.
/// <para>
/// <b>Why REAL Postgres and not the InMemory provider.</b> The guard is
/// <c>j.Status != JobAdStatus.Erased</c> over a strongly-typed value object with a
/// <c>HasConversion</c> to <c>varchar(20)</c>. Every proven comparison in this repo is <c>==</c>;
/// <c>!=</c> inside a translated <c>Where</c> was not. <b>InMemory would pass this test whether or
/// not Postgres can translate the predicate</b> — LINQ-to-objects always honours record equality —
/// so a green unit test would prove nothing about the SQL production emits. That is this repo's own
/// rule and it is the reason this file exists at this level.
/// </para>
/// <para>
/// <b>Each test makes its guard FIRE.</b> A guard that has never once fired in a test has not been
/// tested; it has been typed. Both branches are asserted: the erased ad is excluded AND a
/// non-erased control still comes back. On the SAVED-ADS surfaces the control is deliberately
/// ARCHIVED (#805-3/#821 restored archived ads there, and `== Active` would re-kill them); on the
/// MATCH LIST it is ACTIVE, because #864 (2026-07-14) made that surface an allow-list — a listed
/// grade is a recommendation — and its own tests own the archived-exclusion claims.
/// </para>
/// </remarks>
[Collection("Api")]
public sealed class ErasedAdReadPathTests(ApiFactory factory)
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);

    private (AppDbContext Db, IServiceScope Scope) NewScope()
    {
        var scope = factory.Services.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope);
    }

    private static IDateTimeProvider ClockAt(DateTimeOffset at)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(at);
        return clock;
    }

    private static ICurrentUser UserWith(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    /// <summary>Seeds an ad through the real aggregate factory. <paramref name="erase"/> tombstones it.</summary>
    private static async Task<JobAd> SeedAdAsync(
        AppDbContext db, string title, string company, bool erase, CancellationToken ct,
        bool archive = false)
    {
        var clock = ClockAt(T0);
        var externalId = $"erased-readpath-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: "beskrivning",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: T0,
            expiresAt: T0.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);

        // Erase through the PRODUCTION transition, never by writing columns (#843).
        if (archive)
            jobAd.Archive(clock).IsSuccess.ShouldBeTrue();

        if (erase)
            jobAd.Erase(clock).IsSuccess.ShouldBeTrue();

        if (archive || erase)
            await db.SaveChangesAsync(ct);

        return jobAd;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // 1. /matchningar — the in-app list
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyMatches_drops_an_erased_ad_and_KEEPS_an_active_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var (db, scope) = NewScope();
        using (scope)
        {
            var erased = await SeedAdAsync(db, "Raderad roll", "Raderat bolag", erase: true, ct);
            // ACTIVE control, as of #864 (merged 2026-07-14): the match LIST is an ALLOW-list
            // (`Status == Active`) — a grade in a list is a recommendation, and #864 stopped it
            // being made for archived ads. An earlier version of this test used an ARCHIVED
            // control and asserted it came back; that was true under the round-2..5 `!= Erased`
            // deny-list, which #864's allow-list deliberately superseded (its comment names the
            // erased tombstone as the reason a deny-list is wrong). The erased-exclusion claim —
            // this file's own concern — is unchanged and asserted below; the archived-exclusion
            // claims belong to #864's own tests on main.
            var active = await SeedAdAsync(db, "Aktiv roll", "Aktivt bolag", erase: false, ct);

            db.JobSeekers.Add(JobSeeker.Register(userId, "Erased Read Path", ClockAt(T0)).Value);
            db.UserJobAdMatches.Add(
                UserJobAdMatch.Create(userId, erased.Id, NotifiableMatchGrade.Strong, ["csharp"], ClockAt(T0)).Value);
            db.UserJobAdMatches.Add(
                UserJobAdMatch.Create(userId, active.Id, NotifiableMatchGrade.Strong, ["csharp"], ClockAt(T0)).Value);
            await db.SaveChangesAsync(ct);

            // The REAL handler. Re-implementing the filter in the test would prove only that the
            // test can filter.
            var handler = new GetMyMatchesQueryHandler(db, UserWith(userId));
            var matches = await handler.Handle(new GetMyMatchesQuery(), ct);

            matches.Select(m => m.JobAdId).ShouldNotContain(erased.Id.Value,
                "an erased ad joins fine — it is a row, not a hole — and would render an empty title "
                + "with the company '[raderad]' on the user's screen.");

            matches.Select(m => m.JobAdId).ShouldContain(active.Id.Value,
                "the control: the query returns rows at all, so the exclusion above is a guard "
                + "firing, not an empty result set agreeing with anything.");
        }
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // 2. Sparade annonser — the guard THIS PR added, which had zero tests
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListSavedJobAds_renders_an_erased_ad_as_the_orphan_row_and_KEEPS_an_archived_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var (db, scope) = NewScope();
        using (scope)
        {
            var seeker = JobSeeker.Register(userId, "Erased Read Path", ClockAt(T0)).Value;
            db.JobSeekers.Add(seeker);
            await db.SaveChangesAsync(ct);

            // Saved while still live — which is the real sequence: she bookmarks it, and the erasure
            // happens later.
            var toErase = await SeedAdAsync(db, "Raderad roll", "Raderat bolag", erase: false, ct);
            var archived = await SeedAdAsync(db, "Arkiverad roll", "Arkiverat bolag", erase: false, ct,
                archive: true);

            db.SavedJobAds.Add(SavedJobAd.Save(seeker.Id, toErase.Id, T0));
            db.SavedJobAds.Add(SavedJobAd.Save(seeker.Id, archived.Id, T0));
            await db.SaveChangesAsync(ct);

            toErase.Erase(ClockAt(T0)).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);

            var handler = new ListSavedJobAdsQueryHandler(db, UserWith(userId));
            var saved = await handler.Handle(new ListSavedJobAdsQuery(), ct);

            var erasedRow = saved.Single(s => s.JobAdId == toErase.Id.Value);
            erasedRow.JobAd.ShouldBeNull(
                "an erased ad must project as NULL — the same orphan row as a missing ad "
                + "('Annonsen är borttagen'). Without the guard it renders as a normal card with an "
                + "empty title and the company '[raderad]'.");

            var archivedRow = saved.Single(s => s.JobAdId == archived.Id.Value);
            archivedRow.JobAd.ShouldNotBeNull(
                "an ARCHIVED saved ad still renders — #805-3 removed the Active filter here on "
                + "purpose, and `== Active` would undo it.");
        }
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // 3. The WRITE side — a tombstone cannot be bookmarked
    // ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveJobAd_refuses_an_erased_ad_and_ACCEPTS_an_archived_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var (db, scope) = NewScope();
        using (scope)
        {
            db.JobSeekers.Add(JobSeeker.Register(userId, "Erased Read Path", ClockAt(T0)).Value);
            await db.SaveChangesAsync(ct);

            var erased = await SeedAdAsync(db, "Raderad roll", "Raderat bolag", erase: true, ct);
            var archived = await SeedAdAsync(db, "Arkiverad roll", "Arkiverat bolag", erase: false, ct,
                archive: true);

            var handler = new SaveJobAdCommandHandler(
                db, UserWith(userId), ClockAt(T0),
                scope.ServiceProvider.GetRequiredService<IDbExceptionInspector>());

            var refused = await handler.Handle(new SaveJobAdCommand(erased.Id.Value), ct);
            refused.IsFailure.ShouldBeTrue(
                "reachable from a stale list, an open tab or a bookmarked URL. A read-side guard "
                + "apologising for a write we could simply refuse is not a design.");
            refused.Error.Kind.ShouldBe(ErrorKind.NotFound);

            var accepted = await handler.Handle(new SaveJobAdCommand(archived.Id.Value), ct);
            accepted.IsSuccess.ShouldBeTrue(
                "and an ARCHIVED ad is still bookmarkable — the guard is `!= Erased`, not `== Active`.");
        }
    }
}
