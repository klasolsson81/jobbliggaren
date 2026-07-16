using System.Reflection;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// FTS locks L1 + L4 (#842 Tier A, CTO re-bind R3): removing the recruiter from the reverse-lookup
/// index is THE sharpest exposure in the issue, and each lock pins one structural guarantee. L2/L3
/// (extraction and the funnel round-trip) are integration-level and live in
/// <c>RecruiterContactIngestTests</c>.
/// </summary>
public class RecruiterContactFtsLockTests
{
    /// <summary>
    /// L1 — <c>search_vector</c> is generated from <c>title || description</c> ONLY. If someone
    /// ever adds <c>contacts</c> to the generation expression, the whole Tier-A design inverts:
    /// the bounded, un-indexed carrier becomes reverse-queryable by every logged-in user, which
    /// is exactly the exposure the field exists to end.
    /// </summary>
    [Fact]
    public void L1_search_vector_is_generated_from_title_and_description_only()
    {
        var configSource = File.ReadAllText(
            SourcePath("src/Jobbliggaren.Infrastructure/Persistence/Configurations/JobAdConfiguration.cs"));

        var expressionLine = configSource.Split('\n')
            .FirstOrDefault(l => l.Contains("to_tsvector('swedish'"));

        expressionLine.ShouldNotBeNull(
            "search_vector's HasComputedColumnSql expression has moved — re-pin this lock");
        var line = expressionLine!;
        line.ShouldContain("coalesce(title,'')");
        line.ShouldContain("coalesce(description,'')");
        line.ShouldNotContain("contacts",
            customMessage: "L1 (#842): contacts must NEVER enter the FTS generation expression");
    }

    /// <summary>
    /// L4 — the LIST DTO is structurally incapable of carrying a contact, and the detail
    /// projection is a DISTINCT type. A shared DTO would put ~37k recruiters' structured contacts
    /// on the search wire ~20 per page the day PR4 adds the member (re-bind R2/B2 — the
    /// bulk-harvest hazard). Conventions drift; types do not.
    /// </summary>
    [Fact]
    public void L4_the_list_dto_cannot_carry_a_contact_and_detail_is_a_distinct_type()
    {
        typeof(JobAdDto).ShouldNotBe(typeof(JobAdDetailDto));

        var contactBearing = typeof(JobAdDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase)
                        || p.PropertyType == typeof(AdContacts)
                        || p.PropertyType == typeof(AdContact)
                        || (p.PropertyType.IsGenericType
                            && p.PropertyType.GetGenericArguments().Contains(typeof(AdContact))))
            .ToList();

        contactBearing.ShouldBeEmpty(
            "the LIST DTO must stay structurally contact-incapable (FTS lock L4, re-bind R2)");
    }

    /// <summary>
    /// L4b — the detail DTO carries no contact member EITHER, until PR4 lands the reader
    /// (ADR 0108 §3: a wire field lands WITH its reader). When PR4 arrives, IT deletes this fact
    /// and adds the member + the UI in the same change.
    /// </summary>
    [Fact]
    public void L4b_the_detail_dto_carries_no_contact_member_until_its_reader_exists()
    {
        typeof(JobAdDetailDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty("the wire field lands with its PR4 reader, not before (ADR 0108 §3)");
    }

    private static string SourcePath(string repoRelative)
    {
        // Walk up from the test bin directory to the repo root (the directory holding the .sln).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("could not locate the repo root from the test bin directory");
        return Path.Combine(dir.FullName, repoRelative.Replace('/', Path.DirectorySeparatorChar));
    }
}
