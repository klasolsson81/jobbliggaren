using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobAds.Events;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// GDPR Art. 17 — <see cref="JobAd.Erase"/> and the re-import tombstone (ADR 0106 Tier B, #842).
/// </summary>
public class JobAdEraseTests
{
    private static readonly FakeClock Clock = new();

    // An enskild firma's organisation number IS a personnummer (CLAUDE.md 5). Seeded through the
    // real factory so the tombstone tests below assert against the state ingest actually produces.
    private const string SoleTraderOrgNr = "5509281234";

    private static JobAd ImportedAd(string description = "Kontakta Anna Karlsson, anna@acme.se.") =>
        JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Acme AB").Value,
            description: description,
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            external: ExternalReference.Create(JobSource.Platsbanken, "ext-1").Value,
            rawPayload: """{"id":"ext-1","employer":{"name":"Acme AB"}}""",
            facets: TestFacets.From(organizationNumber: SoleTraderOrgNr),
            publishedAt: Clock.UtcNow,
            expiresAt: null,
            clock: Clock, declaredContacts: []).Value;

    [Fact]
    public void Erase_clears_every_free_text_surface_and_marks_the_ad_Erased()
    {
        var ad = ImportedAd();

        ad.Erase(Clock).IsSuccess.ShouldBeTrue();

        ad.Status.ShouldBe(JobAdStatus.Erased);
        ad.Title.ShouldBeEmpty();
        ad.Description.ShouldBeEmpty();
        ad.Url.ShouldBeEmpty();
        ad.RawPayload.ShouldBeNull();
    }

    /// <summary>
    /// The company name is NOT incidental. An <i>enskild firma</i>'s company name IS a natural
    /// person's name — and the erasure command matches against <c>raw_payload</c>, which carries
    /// <c>employer.name</c>. Leave it, and a request naming that person matches her ad, erases it,
    /// gets told it is done, and leaves the identical string in <c>job_ads.company_name</c>. That is
    /// the #842 defect class, reproduced inside its own fix.
    /// </summary>
    [Fact]
    public void Erase_clears_the_company_name_because_an_enskild_firma_IS_a_person()
    {
        var ad = ImportedAd();

        ad.Erase(Clock);

        ad.Company.Name.ShouldBe(Company.Erased.Name);
        ad.Company.Name.ShouldNotBe("Acme AB");
    }

    /// <summary>
    /// Empty, not null. <c>ExtractedTerms == null</c> means "never extracted" and is what carries
    /// <c>BackfillJobAdExtractedTermsJob</c>'s idempotence (<c>extracted_lexemes IS NULL</c>), so a
    /// nulled value would make an erased ad look un-extracted and the backfill would pick it up.
    /// Empty is also simply true: re-running the extractor over the erased (empty) text yields
    /// exactly zero terms, so the state is what the funnel would produce.
    /// </summary>
    /// <summary>
    /// <b>The erasure of this column used to be FREE, and then it was not.</b>
    /// </summary>
    /// <remarks>
    /// <c>organization_number</c> was a STORED GENERATED column derived from <c>raw_payload</c>, so
    /// nulling the payload nulled the org.nr and <see cref="JobAd.Erase"/> never knew the column
    /// existed. #841 materialised it into an ordinary, ingest-written column that persists
    /// indefinitely — and the erasure silently stopped erasing it, while the cascade registry
    /// certified it destroyed and the Art. 12(3) reply told a named data subject so.
    /// <para>
    /// For an <i>enskild firma</i> that number IS a personnummer (CLAUDE.md §5, the highest-priority
    /// guard in the product). <b>A control that works by accident is not a control.</b>
    /// </para>
    /// </remarks>
    [Fact]
    public void Erase_nulls_the_organisation_number_because_a_sole_traders_org_nr_IS_a_personnummer()
    {
        var ad = ImportedAd();
        ad.OrganizationNumber.ShouldBe(SoleTraderOrgNr,
            "the precondition: ingest DOES store it, so the assertion below is not vacuous.");

        ad.Erase(Clock);

        ad.OrganizationNumber.ShouldBeNull(
            "an Art. 17 erasure that leaves a personnummer in the row, while telling her it was "
            + "destroyed, is the defect this whole issue exists to end.");
    }

    [Fact]
    public void Erase_sets_ExtractedTerms_to_Empty_not_null_so_the_backfill_skips_the_tombstone()
    {
        var ad = ImportedAd();
        ad.SetExtractedTerms(ExtractedTerms.Empty);

        ad.Erase(Clock);

        ad.ExtractedTerms.ShouldNotBeNull(
            "null means 'never extracted' and would put the tombstone back in the backfill's queue.");
        ad.ExtractedTerms.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Erase_raises_JobAdErasedDomainEvent_carrying_the_external_id_and_no_personal_data()
    {
        var ad = ImportedAd();

        ad.Erase(Clock);

        var @event = ad.DomainEvents.OfType<JobAdErasedDomainEvent>().ShouldHaveSingleItem();
        @event.JobAdId.ShouldBe(ad.Id);
        @event.ExternalId.ShouldBe("ext-1",
            "AF's id for a public advertisement is the accountability spine — and it is not her data.");
    }

    [Fact]
    public void Erase_is_refused_on_an_already_erased_ad()
    {
        var ad = ImportedAd();
        ad.Erase(Clock);

        var second = ad.Erase(Clock);

        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe("JobAd.AlreadyErased");
    }

    // ================================================================================
    // THE TOMBSTONE. This is the test that decides whether the erasure is real.
    // ================================================================================

    /// <summary>
    /// <b>The whole contract rests here.</b> The nightly snapshot sync (02:00) and the 10-minute
    /// stream both funnel into <c>UpsertExternalJobAdCommandHandler</c>, which has no hash
    /// short-circuit and calls <c>UpdateFromSource</c> unconditionally. If that method does not
    /// refuse on <c>Erased</c>, the erasure is undone within ≤10 minutes for any ad still listed at
    /// Arbetsförmedlingen — and every Art. 12(3) confirmation we send is false by morning.
    /// <para>
    /// Durability is bought by PLACEMENT: the refusal lives in the aggregate, so both the snapshot
    /// and the stream inherit it, and no suppression ledger is needed (a ledger would store the
    /// recruiter's email in order to keep erasing it).
    /// </para>
    /// </summary>
    [Fact]
    public void UpdateFromSource_REFUSES_on_an_erased_ad_so_the_nightly_sync_cannot_resurrect_her()
    {
        var ad = ImportedAd();
        ad.Erase(Clock);

        // Exactly what the 02:00 sync does: AF still serves the ad, contact block and all.
        var result = ad.UpdateFromSource(
            title: "Backend-utvecklare",
            description: "Kontakta Anna Karlsson, anna@acme.se.",
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            rawPayload: """{"id":"ext-1","employer":{"name":"Acme AB"}}""",
            facets: TestFacets.From(organizationNumber: SoleTraderOrgNr),
            expiresAt: null, declaredContacts: []);

        result.IsFailure.ShouldBeTrue(
            "if this passes, the 02:00 sync writes her address back and we have lied to her.");
        result.Error.Code.ShouldBe("JobAd.Erased");

        ad.Description.ShouldBeEmpty("and nothing may be written before the refusal, either.");
        ad.Title.ShouldBeEmpty();
        ad.RawPayload.ShouldBeNull();
        ad.Status.ShouldBe(JobAdStatus.Erased);
    }

    [Fact]
    public void Archive_is_refused_on_an_erased_ad_because_Erased_is_terminal()
    {
        var ad = ImportedAd();
        ad.Erase(Clock);

        var result = ad.Archive(Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.Erased");
        ad.Status.ShouldBe(JobAdStatus.Erased);
    }

    [Fact]
    public void Erased_is_a_valid_status_value_round_tripping_through_the_converter()
    {
        // job_ads.status is varchar(20) with no CHECK constraint and no PG enum type, so the fourth
        // value costs zero migrations — but only if FromValue knows it, which is what the EF value
        // converter calls on every read.
        var parsed = JobAdStatus.FromValue("Erased");

        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.ShouldBe(JobAdStatus.Erased);
        JobAdStatus.Erased.Value.Length.ShouldBeLessThanOrEqualTo(20);
    }

    [Fact]
    public void FromValue_FailsLoud_OnTheRetiredExpiredValue()
    {
        // #886 / ADR 0111 — the BE twin of the FE zod regression lock. "Expired" was declared,
        // rendered and unreachable for the product's entire history; FromValue retiring its case is
        // what makes a resurrected (or hand-written) 'Expired' row surface as a Validation failure
        // at the EF value converter instead of silently masquerading as a live state. Without this
        // test the failure arm is an untested guarantee: re-adding the case would flip the FE lock
        // red but nothing on the backend, where the value actually enters from the database.
        var result = JobAdStatus.FromValue("Expired");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAdStatus.Invalid");
    }

    private sealed class FakeClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    }
}
