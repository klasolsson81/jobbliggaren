using System.Reflection;
using Jobbliggaren.Infrastructure.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// #1981 PR 2 (security-auditor, probe 9, Minor 1) — every mail template has its subject pinned by
/// <see cref="MailSubject.ShouldCarryNoCodeLinkOrToken"/> in its own test class. A new template fails this test until
/// its tests pin that its subject carries no code, link or token from its body.
/// </summary>
public class EmailSubjectPinCoverageTests
{
    private static readonly string[] PinnedTemplates =
    [
        nameof(EmailTemplates.LoginChallenge),
        nameof(EmailTemplates.MatchNotification),
        nameof(EmailTemplates.FollowedCompanyNotification),
        nameof(EmailTemplates.EmailChangedNotification),
        nameof(EmailTemplates.AccountEmailChangeRequestedNotification),
        nameof(EmailTemplates.FeedbackReceivedNotification),
        nameof(EmailTemplates.FeedbackReply),
    ];

    [Fact]
    public void EveryPublicTemplate_HasItsSubjectPinned()
    {
        var templates = typeof(EmailTemplates)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(EmailTemplates.EmailContent))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        templates.ShouldBe(PinnedTemplates.Order(StringComparer.Ordinal).ToList(),
            "a new template needs a MailSubject.ShouldCarryNoCodeLinkOrToken pin in its tests, then a row here");
    }
}
