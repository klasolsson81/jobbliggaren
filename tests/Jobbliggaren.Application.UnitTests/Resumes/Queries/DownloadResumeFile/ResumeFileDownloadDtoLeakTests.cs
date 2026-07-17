using Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries.DownloadResumeFile;

/// <summary>
/// CV-pivot 5b (security-bind B7) — the pnr flag and its consent evidence never surface on a
/// read DTO. The download transport is pinned to EXACTLY (Content, ContentType, FileName): a
/// future property added to <see cref="ResumeFileDownloadDto"/> (PnrFlagged, the consent
/// stamps, or anything else) must consciously break this pin and argue its wire exposure.
/// </summary>
public class ResumeFileDownloadDtoLeakTests
{
    [Fact]
    public void ResumeFileDownloadDto_CarriesExactlyContentContentTypeFileName()
    {
        var members = typeof(ResumeFileDownloadDto).GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        members.ShouldBe(["Content", "ContentType", "FileName"]);
    }
}
