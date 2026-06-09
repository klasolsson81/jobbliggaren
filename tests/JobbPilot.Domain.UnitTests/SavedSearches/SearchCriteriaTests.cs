using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.SavedSearches;
using Shouldly;

namespace JobbPilot.Domain.UnitTests.SavedSearches;

// Batch 3 — ADR 0042 Beslut B (Accepted): Ssyk/Region string? → IReadOnlyList<string>.
// Q/SortBy oförändrade (ADR 0039 Beslut 3 kärnresonemang hålls). VO-invarianter
// speglar ListJobAdsQueryValidator så en sparad sökning aldrig kan vara mer
// tillåtande än motsvarande live-sökning.
//
// RÖD tills SearchCriteria.cs implementerar list-signatur + fyra invarianter.
// Kompilerar mot ny signatur (annars blockeras impl-bygget).
public class SearchCriteriaTests
{
    // ---------------------------------------------------------------
    // Happy path + minst-ett-kriterium (list-form)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithSsykOnly_ReturnsSuccess()
    {
        var result = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.ShouldBe(["12345"]);
        result.Value.Region.ShouldBeEmpty();
        result.Value.Q.ShouldBeNull();
        result.Value.SortBy.ShouldBe(JobAdSortBy.PublishedAtDesc);
    }

    [Fact]
    public void Create_WithRegionOnly_ReturnsSuccess()
    {
        var result = SearchCriteria.Create(null, ["stockholm_AB"], null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Region.ShouldBe(["stockholm_AB"]);
        result.Value.Ssyk.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithQOnly_ReturnsSuccess()
    {
        var result = SearchCriteria.Create(null, null, "backend", JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Q.ShouldBe("backend");
        result.Value.Ssyk.ShouldBeEmpty();
        result.Value.Region.ShouldBeEmpty();
    }

    [Fact]
    public void Create_WithAllCriteria_ReturnsSuccess()
    {
        var result = SearchCriteria.Create(
            ["12345", "67890"], ["stockholm", "uppsala"], "backend", JobAdSortBy.ExpiresAtAsc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.ShouldBe(["12345", "67890"]);
        result.Value.Region.ShouldBe(["stockholm", "uppsala"]);
        result.Value.Q.ShouldBe("backend");
        result.Value.SortBy.ShouldBe(JobAdSortBy.ExpiresAtAsc);
    }

    [Fact]
    public void Create_WithMultiSsyk_ReturnsSuccess()
    {
        // Genuint Fas 2-produktbehov (ADR 0042): OR-bevakning över yrken.
        var result = SearchCriteria.Create(
            ["systemutvecklare", "frontendutvecklare"], null, null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.Count.ShouldBe(2);
    }

    // ---------------------------------------------------------------
    // Invariant 1 — Normalisering: trim + drop tom/whitespace + distinct
    //                ordinal + sorterad ordinal
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NormalizesSsyk_SortedDistinctOrdinal()
    {
        var result = SearchCriteria.Create(
            ["b", "a", "b", " c "], null, null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        // distinct + sorterad ordinal + trim
        result.Value.Ssyk.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Create_NormalizesRegion_SortedDistinctOrdinal()
    {
        var result = SearchCriteria.Create(
            null, ["uppsala", "stockholm", "uppsala"], null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Region.ShouldBe(["stockholm", "uppsala"]);
    }

    [Fact]
    public void Create_DropsEmptyAndWhitespaceElements()
    {
        // Tomma/whitespace-element droppas; minst ett giltigt kvar → success.
        var result = SearchCriteria.Create(
            ["12345", "", "   ", "67890"], null, null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.ShouldBe(["12345", "67890"]);
    }

    [Fact]
    public void Create_OrdinalSort_IsCaseSensitive()
    {
        // Ordinal-sortering: versaler före gemener (ASCII). Säkerställer
        // deterministisk ordning för jsonb-dedupe oavsett input-ordning.
        var a = SearchCriteria.Create(
            ["zebra", "Apple"], null, null, JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(
            ["Apple", "zebra"], null, null, JobAdSortBy.PublishedAtDesc).Value;

        a.Ssyk.ShouldBe(b.Ssyk);
        // ordinal: 'A' (65) < 'z' (122)
        a.Ssyk[0].ShouldBe("Apple");
    }

    // ---------------------------------------------------------------
    // Invariant 2 — Equality strukturell (SequenceEqual ordinal).
    //                Record + IReadOnlyList får default REFERENS-equality →
    //                MÅSTE överridas. Detta är SavedSearch jsonb-dedupe-grunden.
    // ---------------------------------------------------------------

    [Fact]
    public void TwoCriteria_SameElementsDifferentOrder_AreValueEqual()
    {
        // KRITISKT: jsonb-dedupe vilar på detta. Normalisering gör att samma
        // element i olika ordning producerar strukturellt lika VO:n.
        var a = SearchCriteria.Create(
            ["b", "a"], ["x", "y"], "backend", JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(
            ["a", "b"], ["y", "x"], "backend", JobAdSortBy.PublishedAtDesc).Value;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        a.ShouldBe(b);
    }

    [Fact]
    public void TwoCriteria_DifferentSsykElements_AreNotValueEqual()
    {
        var a = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(["99999"], null, null, JobAdSortBy.PublishedAtDesc).Value;

        a.Equals(b).ShouldBeFalse();
        (a == b).ShouldBeFalse();
        a.ShouldNotBe(b);
    }

    [Fact]
    public void TwoCriteria_DifferentSsykCount_AreNotValueEqual()
    {
        var a = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(["12345", "67890"], null, null, JobAdSortBy.PublishedAtDesc).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void CriteriaDifferingOnlyByQ_AreNotValueEqual()
    {
        var a = SearchCriteria.Create(["12345"], null, "backend", JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(["12345"], null, "frontend", JobAdSortBy.PublishedAtDesc).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void CriteriaDifferingOnlyBySortBy_AreNotValueEqual()
    {
        // SortBy ingår i identiteten (ADR 0039 Beslut 3 hålls).
        var a = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.PublishedAtAsc).Value;

        a.ShouldNotBe(b);
    }

    [Fact]
    public void TrimNormalizedCriteria_AreValueEqualToUntrimmed()
    {
        var a = SearchCriteria.Create(["  12345  "], null, null, JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.PublishedAtDesc).Value;

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void DuplicateElements_NormalizeToSame_AreValueEqual()
    {
        var a = SearchCriteria.Create(["12345", "12345"], null, null, JobAdSortBy.PublishedAtDesc).Value;
        var b = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.PublishedAtDesc).Value;

        a.ShouldBe(b);
    }

    // ---------------------------------------------------------------
    // Invariant 3 — Maxantal-cap = MaxConceptIds per lista (Domain-konstant).
    // C1 (ADR 0067 Platsbanken sök-paritet): cap höjs 10→400 (enhetligt per
    // dimension). Boundary-testerna refererar konstanten, ALDRIG literalen 400,
    // så testerna följer med om MaxConceptIds ändras igen (DRY, CLAUDE.md §5.1).
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithExactlyMaxSsyk_ReturnsSuccess()
    {
        var max = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"ssyk{i}").ToArray();

        var result = SearchCriteria.Create(max, null, null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    [Fact]
    public void Create_WithOneOverMaxSsyk_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"ssyk{i}").ToArray();

        var result = SearchCriteria.Create(overMax, null, null, JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManySsyk");
    }

    [Fact]
    public void Create_WithOneOverMaxRegion_ReturnsFailure()
    {
        var overMax = Enumerable.Range(1, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"reg{i}").ToArray();

        var result = SearchCriteria.Create(null, overMax, null, JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.TooManyRegion");
    }

    [Fact]
    public void Create_CapIsFourHundred_AfterC1Raise()
    {
        // C1 (ADR 0067) låser den nya cap-nivån. Self-dokumenterande grind:
        // bevisar att 10→400-höjningen faktiskt skett (om någon råkar sänka
        // tillbaka konstanten faller detta test).
        SearchCriteria.MaxConceptIds.ShouldBe(400);
    }

    [Fact]
    public void Create_CapAppliesAfterDistinct_MaxPlusOneWithDuplicateUnderCap_ReturnsSuccess()
    {
        // MaxConceptIds+1 råelement varav 1 dubblett → MaxConceptIds distinkta
        // → under cap → success (cap appliceras EFTER distinct-normaliseringen).
        var raw = Enumerable.Range(1, SearchCriteria.MaxConceptIds)
            .Select(i => $"ssyk{i}").ToList();
        raw.Add("ssyk1"); // dubblett

        var result = SearchCriteria.Create(raw, null, null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.Count.ShouldBe(SearchCriteria.MaxConceptIds);
    }

    // ---------------------------------------------------------------
    // Invariant 4 — Tom-invariant generaliserad. Tomma listor + null/
    //                whitespace Q → SearchCriteria.Empty.
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithAllNull_ReturnsEmptyFailure()
    {
        var result = SearchCriteria.Create(null, null, null, JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
    }

    [Fact]
    public void Create_WithEmptyListsAndNullQ_ReturnsEmptyFailure()
    {
        var result = SearchCriteria.Create([], [], null, JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
    }

    [Fact]
    public void Create_WithOnlyWhitespaceElementsAndWhitespaceQ_ReturnsEmptyFailure()
    {
        // Tom efter normalisering (alla element whitespace) + whitespace Q
        // = inget filter = SearchCriteria.Empty.
        var result = SearchCriteria.Create(
            ["", "  "], [" "], "   ", JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.Empty");
    }

    [Fact]
    public void Create_WithEmptyListsButQNonNull_ReturnsSuccess()
    {
        var result = SearchCriteria.Create([], [], "backend", JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.ShouldBeEmpty();
        result.Value.Region.ShouldBeEmpty();
        result.Value.Q.ShouldBe("backend");
    }

    [Fact]
    public void Create_WithOneNonEmptyListAndNullQ_ReturnsSuccess()
    {
        var result = SearchCriteria.Create(["12345"], [], null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // Invariant 5 — Per-element regex ^[A-Za-z0-9_-]{1,32}$
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("has space")]
    [InlineData("åäö")]
    [InlineData("semi;colon")]
    [InlineData("dot.notation")]
    [InlineData("plus+sign")]
    [InlineData("123456789012345678901234567890123")] // 33 tecken > 32
    public void Create_WithInvalidSsykElement_ReturnsFailure(string bad)
    {
        // Ett ogiltigt element bland giltiga → hela Create faller.
        var result = SearchCriteria.Create(
            ["12345", bad], null, null, JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidSsyk");
    }

    [Theory]
    [InlineData("region space")]
    [InlineData("åäö")]
    [InlineData("123456789012345678901234567890123")]
    public void Create_WithInvalidRegionElement_ReturnsFailure(string bad)
    {
        var result = SearchCriteria.Create(
            null, ["stockholm", bad], null, JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidRegion");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ABC-123_xyz")]
    [InlineData("12345678901234567890123456789012")] // exakt 32 tecken
    public void Create_WithValidSsykElementFormat_ReturnsSuccess(string ssyk)
    {
        var result = SearchCriteria.Create([ssyk], null, null, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Ssyk.ShouldBe([ssyk]);
    }

    // ---------------------------------------------------------------
    // Q oförändrat — 2-100 tecken-regeln kvar
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithQTooShort_ReturnsFailure()
    {
        var result = SearchCriteria.Create(null, null, "a", JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidQ");
    }

    [Fact]
    public void Create_WithQTooLong_ReturnsFailure()
    {
        var result = SearchCriteria.Create(
            null, null, new string('x', 101), JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidQ");
    }

    [Theory]
    [InlineData("ab")]                  // exakt min 2
    [InlineData("backend developer")]
    public void Create_WithQAtBoundaries_ReturnsSuccess(string q)
    {
        var result = SearchCriteria.Create(null, null, q, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Q.ShouldBe(q);
    }

    [Fact]
    public void Create_WithQAtMaxLength_ReturnsSuccess()
    {
        var q = new string('x', 100);
        var result = SearchCriteria.Create(null, null, q, JobAdSortBy.PublishedAtDesc);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Q!.Length.ShouldBe(100);
    }

    // ---------------------------------------------------------------
    // SortBy — Enum.IsDefined (oförändrat)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithUndefinedSortBy_ReturnsFailure()
    {
        var result = SearchCriteria.Create(["12345"], null, null, (JobAdSortBy)999);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidSortBy");
    }

    // ---------------------------------------------------------------
    // ADR 0042 Beslut D — Relevance kräver q (relevans utan söktext odefinierad)
    // ---------------------------------------------------------------

    [Fact]
    public void Create_RelevanceSortWithoutQ_ReturnsFailure()
    {
        var result = SearchCriteria.Create(["12345"], null, null, JobAdSortBy.Relevance);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.RelevanceRequiresQ");
    }

    [Fact]
    public void Create_RelevanceSortWithQ_ReturnsSuccess()
    {
        var result = SearchCriteria.Create(null, null, "backend", JobAdSortBy.Relevance);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SortBy.ShouldBe(JobAdSortBy.Relevance);
        result.Value.Q.ShouldBe("backend");
    }
}
