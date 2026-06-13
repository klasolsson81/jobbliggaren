using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobAds.Events;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

public class JobAdTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static (string title, Company company, string description, string url, JobSource source, DateTimeOffset publishedAt) ValidParams()
    {
        var company = Company.Create("Klarna").Value;
        return (
            "Senior Backend Engineer",
            company,
            "Vi söker en senior backend-utvecklare.",
            "https://jobs.klarna.com/job/123",
            JobSource.Manual,
            Clock.UtcNow
        );
    }

    [Fact]
    public void Create_WithValidData_ReturnsSuccess()
    {
        var (title, company, desc, url, source, publishedAt) = ValidParams();
        var result = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe(title);
        result.Value.Status.ShouldBe(JobAdStatus.Active);
    }

    [Fact]
    public void Create_WithEmptyTitle_ReturnsFailure()
    {
        var (_, company, desc, url, source, publishedAt) = ValidParams();
        var result = JobAd.Create("", company, desc, url, source, publishedAt, null, Clock);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.TitleRequired");
    }

    [Fact]
    public void Create_WithInvalidUrl_ReturnsFailure()
    {
        var (title, company, desc, _, source, publishedAt) = ValidParams();
        var result = JobAd.Create(title, company, desc, "not-a-url", source, publishedAt, null, Clock);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.UrlInvalid");
    }

    [Fact]
    public void Create_WithExpiresAtBeforePublishedAt_ReturnsFailure()
    {
        var (title, company, desc, url, source, publishedAt) = ValidParams();
        var expiresAt = publishedAt.AddDays(-1);
        var result = JobAd.Create(title, company, desc, url, source, publishedAt, expiresAt, Clock);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.InvalidDates");
    }

    [Fact]
    public void Create_WithValidData_RaisesJobAdCreatedDomainEvent()
    {
        var (title, company, desc, url, source, publishedAt) = ValidParams();
        var result = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock);
        result.IsSuccess.ShouldBeTrue();
        result.Value.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<JobAdCreatedDomainEvent>()
            .Title.ShouldBe(title);
    }

    [Fact]
    public void Archive_WhenActive_TransitionsToArchived()
    {
        var (title, company, desc, url, source, publishedAt) = ValidParams();
        var jobAd = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock).Value;
        jobAd.ClearDomainEvents();

        var result = jobAd.Archive(Clock);

        result.IsSuccess.ShouldBeTrue();
        jobAd.Status.ShouldBe(JobAdStatus.Archived);
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_ReturnsFailure()
    {
        var (title, company, desc, url, source, publishedAt) = ValidParams();
        var jobAd = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock).Value;
        jobAd.Archive(Clock);

        var result = jobAd.Archive(Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.AlreadyArchived");
    }

    [Fact]
    public void Archive_RaisesJobAdArchivedDomainEvent()
    {
        var (title, company, desc, url, source, publishedAt) = ValidParams();
        var jobAd = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock).Value;
        jobAd.ClearDomainEvents();

        jobAd.Archive(Clock);

        jobAd.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<JobAdArchivedDomainEvent>()
            .JobAdId.ShouldBe(jobAd.Id);
    }

    // ADR 0032 §4 — Import factory + UpdateFromSource state-transition

    private static ExternalReference ValidExternalRef() =>
        ExternalReference.Create(JobSource.Platsbanken, "26500001").Value;

    [Fact]
    public void Import_WithValidData_ReturnsSuccessWithExternalSet()
    {
        var (title, company, desc, url, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();
        const string raw = "{\"id\":\"26500001\",\"headline\":\"Senior Backend Engineer\"}";

        var result = JobAd.Import(title, company, desc, url, external, raw, publishedAt, null, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.External.ShouldBe(external);
        result.Value.RawPayload.ShouldBe(raw);
        result.Value.Source.ShouldBe(JobSource.Platsbanken);
        result.Value.Status.ShouldBe(JobAdStatus.Active);
    }

    [Fact]
    public void Import_RaisesJobAdImportedDomainEvent()
    {
        var (title, company, desc, url, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();
        const string raw = "{\"id\":\"26500001\"}";

        var jobAd = JobAd.Import(title, company, desc, url, external, raw, publishedAt, null, Clock).Value;

        var evt = jobAd.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<JobAdImportedDomainEvent>();
        evt.JobAdId.ShouldBe(jobAd.Id);
        evt.Source.ShouldBe("Platsbanken");
        evt.ExternalId.ShouldBe("26500001");
        evt.Title.ShouldBe(title);
    }

    [Fact]
    public void Import_WithEmptyRawPayload_ReturnsFailure()
    {
        var (title, company, desc, url, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();

        var result = JobAd.Import(title, company, desc, url, external, "", publishedAt, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.RawPayloadRequired");
    }

    [Fact]
    public void Import_WithInvalidUrl_ReturnsFailure()
    {
        var (title, company, desc, _, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();

        var result = JobAd.Import(title, company, desc, "not-a-url", external, "{}", publishedAt, null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.UrlInvalid");
    }

    [Fact]
    public void UpdateFromSource_OnImportedJobAd_RefreshesMutableFields()
    {
        var (title, company, desc, url, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();
        var jobAd = JobAd.Import(title, company, desc, url, external,
            "{\"v\":1}", publishedAt, null, Clock).Value;
        jobAd.ClearDomainEvents();

        var newExpiresAt = publishedAt.AddDays(14);
        var result = jobAd.UpdateFromSource(
            "Updated title", "Updated description",
            "https://jobs.klarna.com/job/123-updated", "{\"v\":2}", newExpiresAt);

        result.IsSuccess.ShouldBeTrue();
        jobAd.Title.ShouldBe("Updated title");
        jobAd.Description.ShouldBe("Updated description");
        jobAd.Url.ShouldBe("https://jobs.klarna.com/job/123-updated");
        jobAd.ExpiresAt.ShouldBe(newExpiresAt);
        jobAd.RawPayload.ShouldBe("{\"v\":2}");
        // ADR 0032 §4 — UpdateFromSource raisar inga events (sync auditeras aggregerat)
        jobAd.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateFromSource_OnManualJobAd_ReturnsFailure()
    {
        var (title, company, desc, url, source, publishedAt) = ValidParams();
        var jobAd = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock).Value;

        var result = jobAd.UpdateFromSource(
            "X", "Y", "https://example.com/x", "{}", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.NotImported");
    }

    [Fact]
    public void UpdateFromSource_WithEmptyRawPayload_ReturnsFailure()
    {
        var (title, company, desc, url, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();
        var jobAd = JobAd.Import(title, company, desc, url, external,
            "{\"v\":1}", publishedAt, null, Clock).Value;

        var result = jobAd.UpdateFromSource(
            "Updated", "Updated desc", "https://example.com/x", "", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.RawPayloadRequired");
    }

    // TD-80 — URL scheme-whitelist (http/https only). Defense-in-depth mot
    // XSS via `javascript:`/`data:`/`vbscript:`/`file:`-schemes som
    // `Uri.TryCreate(UriKind.Absolute)` tidigare accepterade.
    // Source: security-auditor F2-P10 frontend-review 2026-05-13.

    [Theory]
    [InlineData("https://jobs.klarna.com/job/123")]
    [InlineData("http://jobs.klarna.com/job/123")]
    [InlineData("HTTPS://jobs.klarna.com/job/123")] // case-insensitive accept
    [InlineData("HTTP://jobs.klarna.com/job/123")]
    public void Create_WithHttpOrHttpsUrl_ReturnsSuccess(string url)
    {
        var (title, company, desc, _, source, publishedAt) = ValidParams();
        var result = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock);
        result.IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("JAVASCRIPT:alert(1)")] // case-insensitive reject
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://attacker.example.com/")]
    [InlineData("gopher://example.com/")]
    public void Create_WithNonHttpScheme_ReturnsUrlInvalid(string url)
    {
        var (title, company, desc, _, source, publishedAt) = ValidParams();
        var result = JobAd.Create(title, company, desc, url, source, publishedAt, null, Clock);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.UrlInvalid");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,xss")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///c/windows/system32/")]
    public void Import_WithNonHttpScheme_ReturnsUrlInvalid(string url)
    {
        var (title, company, desc, _, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();
        var result = JobAd.Import(title, company, desc, url, external, "{}", publishedAt, null, Clock);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.UrlInvalid");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,xss")]
    public void UpdateFromSource_WithNonHttpScheme_ReturnsUrlInvalid(string maliciousUrl)
    {
        var (title, company, desc, url, _, publishedAt) = ValidParams();
        var external = ValidExternalRef();
        var jobAd = JobAd.Import(title, company, desc, url, external,
            "{\"v\":1}", publishedAt, null, Clock).Value;

        var result = jobAd.UpdateFromSource(
            "Updated", "Updated desc", maliciousUrl, "{\"v\":2}", null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.UrlInvalid");
        // Original URL bevarad — handler avbröts före mutation.
        jobAd.Url.ShouldBe(url);
    }
}
