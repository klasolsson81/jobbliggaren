using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jobbliggaren.Application.UnitTests.Common;

/// <summary>
/// Test double for the production <c>FieldDecryptionMaterializationInterceptor</c>
/// (Fas 4b PR-4): the EF-Ignore'd encrypted Form-B contents (<c>ParsedResume.Content</c>,
/// <c>ResumeVersion.Content</c>) are only populated by the real decryption interceptor —
/// InMemory + AsNoTracking re-materializes them as null. Handlers that DEREFERENCE the
/// content before reaching a mocked engine (the unified-adapter call, ADR 0093 §D8) need
/// this hydrator to exercise their happy path in a unit test; the real decrypt path stays
/// proven end-to-end by the Api/Worker integration tests (house seam, documented in
/// <c>GetParsedResumeQueryHandlerTests</c>). Same write mechanism as production:
/// reflection through the private setter.
/// </summary>
internal sealed class FakeContentHydrationInterceptor(
    ParsedResumeContent? parsedContent = null,
    ResumeContent? resumeContent = null) : IMaterializationInterceptor
{
    public object InitializedInstance(MaterializationInterceptionData materializationData, object instance)
    {
        if (instance is ParsedResume parsed && parsedContent is not null)
        {
            typeof(ParsedResume)
                .GetProperty(nameof(ParsedResume.Content))!
                .SetValue(parsed, parsedContent);
        }

        if (instance is ResumeVersion version && resumeContent is not null)
        {
            typeof(ResumeVersion)
                .GetProperty(nameof(ResumeVersion.Content))!
                .SetValue(version, resumeContent);
        }

        return instance;
    }
}
