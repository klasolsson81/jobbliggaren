using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// #874 — test-only convenience for the required <see cref="JobAdTermExtraction"/> delegate on
/// <see cref="JobAd.Import"/> / <see cref="JobAd.UpdateFromSource"/>. Linked into every test project that
/// constructs an imported <see cref="JobAd"/> (the <c>Compile Include</c> items in the test csproj files,
/// parity <c>TestFacets</c>).
///
/// <para>
/// <b>Why this exists.</b> Most tests seed an imported ad to exercise the READ side (search / matching /
/// attribution) and seed the GIN terms separately via <see cref="JobAd.SetExtractedTerms"/> when they need
/// them — they neither care about nor assert the ingest-time extraction. For them <see cref="None"/> supplies
/// the empty extraction the aggregate now requires to satisfy its compile-time coupling, WITHOUT changing
/// what the test measures (a later <c>SetExtractedTerms</c> simply overwrites the empty seed). A test whose
/// subject IS the ingest extraction passes a real function instead — see <see cref="Returning"/> — or asserts
/// against a real <c>IJobAdKeywordExtractor</c>.
/// </para>
///
/// <para>
/// That a domain test can pass one of these as a plain lambda, with no NSubstitute and no fake DbContext, is
/// exactly what proves the delegate is a value and not a smuggled port (CLAUDE.md §2.4).
/// </para>
/// </summary>
internal static class TestKeywordExtraction
{
    /// <summary>
    /// The named no-op: extraction that yields no terms (parity <see cref="ExtractedTerms.Empty"/>). A pure
    /// function of the post-scrub text that ignores it — the neutral seed for the many read-path tests that
    /// do not assert ingest extraction.
    /// </summary>
    internal static JobAdTermExtraction None => static (_, _) => ExtractedTerms.Empty;

    /// <summary>
    /// Extraction that ignores its input and returns fixed <paramref name="terms"/> — for the rare test that
    /// wants <see cref="JobAd.Import"/> / <see cref="JobAd.UpdateFromSource"/> itself to seed specific terms.
    /// </summary>
    internal static JobAdTermExtraction Returning(ExtractedTerms terms) => (_, _) => terms;
}
