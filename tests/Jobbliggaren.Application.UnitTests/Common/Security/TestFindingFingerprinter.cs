using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.UnitTests.Common.Security;

/// <summary>
/// The REAL <see cref="HmacFindingFingerprinter"/> (#692) under a FIXED deterministic test pepper — the
/// shared <see cref="IFindingFingerprinter"/> every handler/reconciler test injects. Using the
/// production type (not a fake) keeps the tests faithful: a fingerprint the handler-under-test stores
/// and one the test recomputes agree because both go through this same instance under the same pepper,
/// and the output is a real 64-lowercase-hex HMAC that satisfies <c>Resume.IsValidFingerprint</c>.
/// The pepper is fixed test material (bytes 200..231), distinct from every production and other
/// test key so nothing can pass by peppering with the wrong one.
/// </summary>
internal static class TestFindingFingerprinter
{
    internal static readonly IFindingFingerprinter Instance =
        new HmacFindingFingerprinter(Options.Create(new CvReviewFingerprintPseudonymizationOptions
        {
            PepperBase64 = Convert.ToBase64String([.. Enumerable.Range(200, 32).Select(i => (byte)i)]),
        }));

    internal static string Compute(RubricVersion version, CvCriterionVerdict verdict) =>
        Instance.Compute(version, verdict);
}
