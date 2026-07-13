using System.Globalization;
using Jobbliggaren.Domain.CompanyWatches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// #560 kriterie-vågen PR-1 (senior-cto-advisor Fork A1/B1 2026-07-12, ADR 0105) — invariants for
/// <see cref="CompanyWatchCriteriaSpec"/>: the BOTH-axes-required invariant with SEPARATE per-axis
/// error codes, the shared normalization (trim → drop blank → distinct ordinal → sort ordinal), the
/// per-element format guard (5-digit SNI vs 4-digit kommun — two different namespaces, RF-4), the
/// per-axis DoS caps, structural equality (the EF <c>text[]</c> value comparison rests on it), and
/// the copy-semantics of <see cref="CompanyWatchCriteriaSpec.FromTrusted"/>.
///
/// <para>
/// <b>The leading zero is the one that bites.</b> "0180" is Stockholm's SCB kommun code. Any place
/// the pipeline treats a code as a NUMBER instead of a string, it silently becomes "180" — a code
/// that belongs to no kommun — and the criterion matches nothing without ever failing. Every axis of
/// this file (normalize, format, sort, equality) is therefore pinned with a leading-zero code.
/// </para>
/// </summary>
public class CompanyWatchCriteriaSpecTests
{
    // Real codes, deliberately: 62010 = "Dataprogrammering", 01110 = "Odling av spannmål" (the
    // leading-zero SNI), 0180 = Stockholm (the leading-zero kommun), 1480 = Göteborg.
    private const string SniIt = "62010";
    private const string SniItConsulting = "62020";
    private const string SniLeadingZero = "01110";
    private const string KommunStockholm = "0180";
    private const string KommunGoteborg = "1480";

    // ---------------------------------------------------------------
    // Create — the BOTH-axes-required invariant (Fork B1), per-axis error codes
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithBothAxesPopulated_Succeeds()
    {
        var result = CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.ShouldBe([SniIt]);
        result.Value.MunicipalityCodes.ShouldBe([KommunStockholm]);
    }

    [Fact]
    public void Create_WithNullSniAxis_FailsWithSniRequired()
    {
        var result = CompanyWatchCriteriaSpec.Create(null, [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.SniRequired");
    }

    [Fact]
    public void Create_WithNullMunicipalityAxis_FailsWithMunicipalityRequired()
    {
        // SEPARATE code from the SNI axis — the picker (PR-3) points at the axis the user actually
        // left blank. One shared "Empty" code would leave the FE guessing.
        var result = CompanyWatchCriteriaSpec.Create([SniIt], null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.MunicipalityRequired");
    }

    [Fact]
    public void Create_WithEmptySniAxis_FailsWithSniRequired()
    {
        var result = CompanyWatchCriteriaSpec.Create([], [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.SniRequired");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_WithWhitespaceOnlySniAxis_FailsWithSniRequired(string blank)
    {
        // Normalization runs BEFORE the invariant check: an axis whose only entry is blank is an
        // EMPTY axis, never a one-element one. Without this, [""] — what a form emits when the user
        // clears the last chip — would be stored as a criterion matching literally nothing.
        var result = CompanyWatchCriteriaSpec.Create([blank], [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.SniRequired");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_WithWhitespaceOnlyMunicipalityAxis_FailsWithMunicipalityRequired(string blank)
    {
        var result = CompanyWatchCriteriaSpec.Create([SniIt], [blank]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.MunicipalityRequired");
    }

    [Fact]
    public void Create_WithBothAxesBlank_ReportsTheSniAxisFirst()
    {
        // Deterministic reporting order (SNI is checked first) — the FE surfaces one error at a
        // time, so which one it gets must not depend on evaluation luck.
        var result = CompanyWatchCriteriaSpec.Create(["  "], [""]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.SniRequired");
    }

    // ---------------------------------------------------------------
    // Normalization — identical on both axes; the leading zero survives
    // ---------------------------------------------------------------

    [Fact]
    public void Create_NormalizesSniCodes_TrimDistinctSortedOrdinal()
    {
        var result = CompanyWatchCriteriaSpec.Create(
            [$" {SniItConsulting} ", SniIt, SniItConsulting, "", "  ", SniLeadingZero],
            [KommunStockholm]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.ShouldBe([SniLeadingZero, SniIt, SniItConsulting]);
    }

    [Fact]
    public void Create_NormalizesMunicipalityCodes_TrimDistinctSortedOrdinal()
    {
        var result = CompanyWatchCriteriaSpec.Create(
            [SniIt],
            [$" {KommunGoteborg} ", KommunStockholm, KommunGoteborg, "", "   "]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityCodes.ShouldBe([KommunStockholm, KommunGoteborg]);
    }

    [Fact]
    public void Create_PreservesLeadingZeroes_OnBothAxes()
    {
        // THE regression pin. Codes are STRINGS end-to-end (text[] columns, ordinal comparison, no
        // parse anywhere). The moment any layer round-trips one through an int, "0180" becomes "180"
        // — Stockholm silently stops being Stockholm and the criterion matches nothing, loudly
        // succeeding all the way. Ordinal sort also puts the leading-zero code FIRST, which is what
        // the assertion order below encodes.
        var result = CompanyWatchCriteriaSpec.Create(
            [SniIt, SniLeadingZero], [KommunGoteborg, KommunStockholm]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.ShouldBe(["01110", "62010"]);
        result.Value.MunicipalityCodes.ShouldBe(["0180", "1480"]);
        result.Value.MunicipalityCodes.ShouldNotContain("180",
            "kommunkoden är en STRÄNG — en ledande nolla som tappas gör Stockholm till ingenting");
    }

    // ---------------------------------------------------------------
    // Per-element format — default-deny, and the two namespaces are NOT interchangeable
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("6201")]        // 4 digits — that is a KOMMUN code, not an SNI code (RF-4 namespaces)
    [InlineData("620100")]      // 6 digits
    [InlineData("62")]          // an SNI SECTION — expanded to leaves by the picker, never stored raw (Fork B1)
    [InlineData("6201a")]
    [InlineData("620 10")]
    [InlineData("62-010")]
    [InlineData("62010; DROP")]
    public void Create_WithInvalidSniCode_Fails(string invalid)
    {
        var result = CompanyWatchCriteriaSpec.Create([invalid], [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.InvalidSniCode");
    }

    [Theory]
    [InlineData("018")]         // 3 digits
    [InlineData("01800")]       // 5 digits — that is an SNI code, not a kommun code (RF-4 namespaces)
    [InlineData("018a")]
    [InlineData("01 80")]
    [InlineData("0180; DROP")]
    public void Create_WithInvalidMunicipalityCode_Fails(string invalid)
    {
        var result = CompanyWatchCriteriaSpec.Create([SniIt], [invalid]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.InvalidMunicipalityCode");
    }

    [Fact]
    public void Create_WithOneInvalidCodeAmongValidOnes_Fails()
    {
        // Default-deny is per ELEMENT, not per list: a single bad code sinks the whole spec rather
        // than being silently dropped (a silently dropped code is a criterion the user did not ask
        // for, matching a set they did not choose).
        var result = CompanyWatchCriteriaSpec.Create([SniIt, "nope", SniItConsulting], [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.InvalidSniCode");
    }

    [Fact]
    public void Create_WithTrailingNewlineOnACode_StripsIt_AndStoresNoNewline()
    {
        // The security property that actually holds: NO newline can reach the stored payload.
        // Note WHY it holds — normalization's Trim() strips the trailing newline BEFORE the regex
        // ever sees the value, so the code is accepted in its cleaned form ("62010"), not rejected.
        // The `\z` anchor (rather than `$`, which in .NET also matches before a trailing newline) is
        // therefore defence-in-depth BEHIND normalization, not the thing doing the work here. What
        // Trim cannot rescue — an EMBEDDED newline — is rejected outright by the test below.
        var result = CompanyWatchCriteriaSpec.Create(["62010\n"], [$"{KommunStockholm}\r\n"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.ShouldBe([SniIt]);
        result.Value.MunicipalityCodes.ShouldBe([KommunStockholm]);
        result.Value.SniCodes.ShouldNotContain(c => c.Contains('\n'),
            "ingen radbrytning får överleva in i den lagrade payloaden");
    }

    [Theory]
    [InlineData("٦٢٠١٠")]  // ٦٢٠١٠ — Arabic-Indic digits
    [InlineData("６２０１０")]  // ６２０１０ — fullwidth digits
    public void Create_WithNonAsciiDigitsInSniCode_Fails(string nonAscii)
    {
        // REGRESSION PIN (bug found by test-writer probe, 2026-07-13). The guard was `^\d{5}\z`,
        // and in .NET `\d` means `\p{Nd}` — the WHOLE Unicode decimal-digit category. Both codes
        // below satisfied it, so they passed the domain guard, got stored in sni_codes, and could
        // then never overlap the ASCII-only SCB register: the criterion would match NOTHING, and
        // never say so. A silent miss is this product's cardinal sin, so the pattern is now
        // ASCII-explicit ([0-9]). Without that, this test fails.
        var result = CompanyWatchCriteriaSpec.Create([nonAscii], [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.InvalidSniCode");
    }

    [Theory]
    [InlineData("٠١٨٠")]  // ٠١٨٠ — Arabic-Indic digits
    [InlineData("０１８０")]  // ０１８０ — fullwidth digits
    public void Create_WithNonAsciiDigitsInMunicipalityCode_Fails(string nonAscii)
    {
        // Same hole on the kommun axis — both axes are ASCII-explicit, not just the SNI one.
        var result = CompanyWatchCriteriaSpec.Create([SniIt], [nonAscii]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.InvalidMunicipalityCode");
    }

    [Theory]
    [InlineData("62010\n62020")]     // a smuggled second line
    [InlineData("62010\nDROP TABLE")]
    public void Create_WithEmbeddedNewlineInSniCode_Fails(string smuggled)
    {
        // Trim() only strips the ENDS — an embedded newline survives normalization and must be
        // rejected by the anchored, default-deny format guard. This is the one a `$`-anchored,
        // multiline-ish read of the pattern could have let past.
        var result = CompanyWatchCriteriaSpec.Create([smuggled], [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.InvalidSniCode");
    }

    // ---------------------------------------------------------------
    // Caps — DoS ceilings, PER AXIS, counted AFTER normalization
    // ---------------------------------------------------------------

    [Fact]
    public void Create_WithExactlyMaxSniCodes_Succeeds()
    {
        // On the boundary is legitimate ("bevaka hela min bransch") — the cap is the size of the
        // universe, so a whole-industry selection must never bite it.
        var result = CompanyWatchCriteriaSpec.Create(
            FiveDigitCodes(CompanyWatchCriteriaSpec.MaxSniCodes), [KommunStockholm]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.Count.ShouldBe(CompanyWatchCriteriaSpec.MaxSniCodes);
    }

    [Fact]
    public void Create_WithOneOverMaxSniCodes_Fails()
    {
        var result = CompanyWatchCriteriaSpec.Create(
            FiveDigitCodes(CompanyWatchCriteriaSpec.MaxSniCodes + 1), [KommunStockholm]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.TooManySniCodes");
    }

    [Fact]
    public void Create_WithExactlyMaxMunicipalityCodes_Succeeds()
    {
        // 290 = exactly the Swedish kommun universe, so "hela Sverige" stays expressible.
        var result = CompanyWatchCriteriaSpec.Create(
            [SniIt], FourDigitCodes(CompanyWatchCriteriaSpec.MaxMunicipalityCodes));

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityCodes.Count.ShouldBe(CompanyWatchCriteriaSpec.MaxMunicipalityCodes);
    }

    [Fact]
    public void Create_WithOneOverMaxMunicipalityCodes_Fails()
    {
        var result = CompanyWatchCriteriaSpec.Create(
            [SniIt], FourDigitCodes(CompanyWatchCriteriaSpec.MaxMunicipalityCodes + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("CompanyWatchCriteriaSpec.TooManyMunicipalityCodes");
    }

    [Fact]
    public void Create_WithBothAxesAtTheirCapSimultaneously_Succeeds()
    {
        // The caps are PER AXIS, not a shared budget: a whole-industry × whole-Sweden criterion is
        // unusual but legitimate, and must not be rejected by an accidental sum-cap.
        var result = CompanyWatchCriteriaSpec.Create(
            FiveDigitCodes(CompanyWatchCriteriaSpec.MaxSniCodes),
            FourDigitCodes(CompanyWatchCriteriaSpec.MaxMunicipalityCodes));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_WithDuplicatesPushingRawCountOverTheCap_Succeeds()
    {
        // The cap is counted on the NORMALIZED list. A client that re-sends the same code twice has
        // not selected more industries — rejecting it would be a cap that bites on transport noise.
        var atCap = FiveDigitCodes(CompanyWatchCriteriaSpec.MaxSniCodes).ToList();
        var withDuplicates = atCap.Concat(atCap.Take(200)).ToList();
        withDuplicates.Count.ShouldBeGreaterThan(CompanyWatchCriteriaSpec.MaxSniCodes);

        var result = CompanyWatchCriteriaSpec.Create(withDuplicates, [KommunStockholm]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SniCodes.Count.ShouldBe(CompanyWatchCriteriaSpec.MaxSniCodes);
    }

    // ---------------------------------------------------------------
    // Structural equality — the EF text[] value comparison rests on it
    // ---------------------------------------------------------------

    [Fact]
    public void Equals_SameCodesInDifferentOrder_AreEqualAfterNormalization()
    {
        // A record with IReadOnlyList members gets REFERENCE equality by default. The explicit
        // structural Equals/GetHashCode is what makes "the same selection, keyed in a different
        // order" one and the same spec.
        var a = CompanyWatchCriteriaSpec.Create(
            [SniItConsulting, $" {SniIt} "], [KommunGoteborg, KommunStockholm]).Value;
        var b = CompanyWatchCriteriaSpec.Create(
            [SniIt, SniItConsulting, SniItConsulting], [KommunStockholm, KommunGoteborg]).Value;

        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_SpecsDifferingOnlyInSniAxis_AreNotEqual()
    {
        var it = CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value;
        var consulting = CompanyWatchCriteriaSpec.Create([SniItConsulting], [KommunStockholm]).Value;

        it.Equals(consulting).ShouldBeFalse();
        it.GetHashCode().ShouldNotBe(consulting.GetHashCode());
    }

    [Fact]
    public void Equals_SpecsDifferingOnlyInMunicipalityAxis_AreNotEqual()
    {
        // Regression pin for the EF VALUE COMPARISON on the kommun_codes column: if equality ignored
        // the kommun axis, EF change detection would call a kommun-only edit a no-op and SILENTLY
        // never persist the user's new selection — the write "succeeds" and changes nothing.
        var stockholm = CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value;
        var goteborg = CompanyWatchCriteriaSpec.Create([SniIt], [KommunGoteborg]).Value;

        stockholm.Equals(goteborg).ShouldBeFalse();
        stockholm.GetHashCode().ShouldNotBe(goteborg.GetHashCode());
    }

    [Fact]
    public void Equals_SupersetOfCodes_IsNotEqualToItsSubset()
    {
        var one = CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value;
        var two = CompanyWatchCriteriaSpec.Create([SniIt, SniItConsulting], [KommunStockholm]).Value;

        one.Equals(two).ShouldBeFalse();
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        CompanyWatchCriteriaSpec.Create([SniIt], [KommunStockholm]).Value
            .Equals(null).ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // FromTrusted — the storage/rehydration path: copies, never validates, never throws
    // ---------------------------------------------------------------

    [Fact]
    public void FromTrusted_MutatingTheSourceListAfterwards_DoesNotChangeTheSpec()
    {
        // THE aliasing pin. The aggregate's backing lists are MUTABLE (EF materializes into them and
        // UpdateCriteria Clears/AddRanges them in place). If FromTrusted aliased them instead of
        // copying, this value object's immutability would be fiction: a later UpdateCriteria would
        // silently rewrite a spec another caller was already holding.
        var sni = new List<string> { SniIt };
        var kommun = new List<string> { KommunStockholm };
        var spec = CompanyWatchCriteriaSpec.FromTrusted(sni, kommun);

        sni.Clear();
        sni.Add("99999");
        kommun.Clear();
        kommun.Add("9999");

        spec.SniCodes.ShouldBe([SniIt], "specen måste ha KOPIERAT källistan, inte aliasat den");
        spec.MunicipalityCodes.ShouldBe([KommunStockholm]);
    }

    [Fact]
    public void FromTrusted_RebuildsASpecEqualToTheCreatedOne()
    {
        // The rehydration round-trip: what Create normalized and the columns stored comes back as
        // the SAME value object (structural equality, not reference).
        var created = CompanyWatchCriteriaSpec.Create(
            [SniItConsulting, SniIt], [KommunGoteborg, KommunStockholm]).Value;

        var rehydrated = CompanyWatchCriteriaSpec.FromTrusted(
            created.SniCodes, created.MunicipalityCodes);

        rehydrated.ShouldBe(created);
        rehydrated.GetHashCode().ShouldBe(created.GetHashCode());
    }

    [Fact]
    public void FromTrusted_DoesNotValidate_AndNeverThrowsOnDegenerateStoredState()
    {
        // Deliberate contract, and load-bearing: the aggregate's Criteria GETTER calls FromTrusted on
        // EVERY read. A validating FromTrusted would turn a single bad row (or an empty text[] left
        // by some future migration) into an exception on every query that touches it — a read path
        // that throws is far worse than a row that is merely wrong. The invariant is enforced on the
        // WRITE path (Create), which is where it can still be reported to the user.
        var degenerate = CompanyWatchCriteriaSpec.FromTrusted([], []);

        degenerate.SniCodes.ShouldBeEmpty();
        degenerate.MunicipalityCodes.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // Helpers — distinct, well-formed code sets of an exact size
    // ---------------------------------------------------------------

    // 10000..(10000+count-1) — every value is exactly 5 digits.
    private static List<string> FiveDigitCodes(int count) =>
        [.. Enumerable.Range(10000, count).Select(i => i.ToString(CultureInfo.InvariantCulture))];

    // 1000..(1000+count-1) — every value is exactly 4 digits.
    private static List<string> FourDigitCodes(int count) =>
        [.. Enumerable.Range(1000, count).Select(i => i.ToString(CultureInfo.InvariantCulture))];
}
