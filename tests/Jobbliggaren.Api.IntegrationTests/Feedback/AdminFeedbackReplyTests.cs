using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.Feedback.FeedbackKit;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

/// <summary>
/// #2069 — an administrator's reply to the person behind a submission, through the real host and Postgres: the
/// recorded outcome is a 200 whatever it was, the same key replays it without a second mail, the detail lists the
/// reply, and the audit row never carries the text. The provider's refusals are the dispositions the transactional
/// arm itself sets. Every submission here is on industry-watches, a page no counting test in the collection owns.
/// </summary>
[Collection("Api")]
public sealed class AdminFeedbackReplyTests(ApiFactory factory)
{
    private const string Text = "Tack för din feedback.\n\nVi har rättat filtret.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Admin, Reporter Reporter, Guid Id)> SubmittedAsync()
    {
        var admin = (await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct)).Client;
        var reporter = await ReporterAsync(factory, Ct);
        var id = await SubmitNewAsync(reporter.Client, "industry-watches", 2, "Bevakningen hittade inte min bransch.", Ct);
        return (admin, reporter, id);
    }

    private static Task<HttpResponseMessage> ReplyAsync(HttpClient admin, Guid id, Guid key, string? text = Text) =>
        admin.PostAsJsonAsync(RepliesPath(id), new { replyKey = key, text }, Ct);

    private int RepliesSentTo(string email) =>
        factory.Emails.Sent.Count(mail => mail.Kind == RecordedEmailKind.FeedbackReply && mail.ToEmail == email);

    private async Task<IReadOnlyList<(string EventType, string? Payload)>> AuditRowsAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogEntries.AsNoTracking()
            .Where(row => row.AggregateId == id && row.EventType == "Admin.FeedbackReplied")
            .Select(row => new ValueTuple<string, string?>(row.EventType, row.Payload))
            .ToListAsync(Ct);
    }

    [Fact]
    public async Task A_reply_is_mailed_to_the_reporter_listed_in_the_detail_and_audited_without_its_text()
    {
        var (admin, reporter, id) = await SubmittedAsync();

        using var response = await ReplyAsync(admin, id, Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var outcome = await JsonAsync(response, Ct);
        outcome.GetProperty("state").GetString().ShouldBe("Accepted");
        outcome.GetProperty("replayed").GetBoolean().ShouldBeFalse();
        RepliesSentTo(reporter.Email).ShouldBe(1);

        using var detail = await admin.GetAsync(DetailPath(id), Ct);
        var reply = (await JsonAsync(detail, Ct)).GetProperty("replies").EnumerateArray().ShouldHaveSingleItem();
        reply.GetProperty("id").GetGuid().ShouldBe(outcome.GetProperty("replyId").GetGuid());
        reply.GetProperty("text").GetString().ShouldBe(Text);
        reply.GetProperty("state").GetString().ShouldBe("Accepted");
        reply.GetProperty("completedAt").ValueKind.ShouldBe(JsonValueKind.String);

        var audit = (await AuditRowsAsync(id)).ShouldHaveSingleItem();
        (audit.Payload ?? "").ShouldNotContain("rättat");
    }

    [Fact]
    public async Task The_same_key_again_replays_the_outcome_without_a_second_mail()
    {
        var (admin, reporter, id) = await SubmittedAsync();
        var key = Guid.NewGuid();
        using var first = await ReplyAsync(admin, id, key);
        var firstId = (await JsonAsync(first, Ct)).GetProperty("replyId").GetGuid();

        using var again = await ReplyAsync(admin, id, key);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        var replayed = await JsonAsync(again, Ct);
        replayed.GetProperty("replyId").GetGuid().ShouldBe(firstId);
        replayed.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        RepliesSentTo(reporter.Email).ShouldBe(1);
        (await AuditRowsAsync(id)).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(EmailDeliveryDisposition.NotAccepted, "Failed")]
    [InlineData(EmailDeliveryDisposition.Unknown, "Unknown")]
    public async Task A_provider_refusal_is_a_recorded_outcome_not_an_error(
        EmailDeliveryDisposition disposition, string expected)
    {
        var (admin, _, id) = await SubmittedAsync();

        using (factory.Emails.Refusing(RecordedEmailKind.FeedbackReply, disposition))
        {
            using var response = await ReplyAsync(admin, id, Guid.NewGuid());

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await JsonAsync(response, Ct)).GetProperty("state").GetString().ShouldBe(expected);
        }

        using var detail = await admin.GetAsync(DetailPath(id), Ct);
        (await JsonAsync(detail, Ct)).GetProperty("replies").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("state").GetString().ShouldBe(expected);
    }

    [Fact]
    public async Task No_text_is_refused_before_anything_is_saved()
    {
        var (admin, reporter, id) = await SubmittedAsync();

        using var response = await ReplyAsync(admin, id, Guid.NewGuid(), "   ");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.ReplyEmpty");
        RepliesSentTo(reporter.Email).ShouldBe(0);
        (await AuditRowsAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_host_that_cannot_deliver_refuses_before_anything_is_saved()
    {
        var (admin, reporter, id) = await SubmittedAsync();

        using (factory.Emails.Incapable())
        {
            using var response = await ReplyAsync(admin, id, Guid.NewGuid());

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.ReplyUnavailable");
        }

        RepliesSentTo(reporter.Email).ShouldBe(0);
        (await AuditRowsAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_submission_is_not_found()
    {
        var admin = (await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct)).Client;

        using var response = await ReplyAsync(admin, Guid.NewGuid(), Guid.NewGuid());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.NotFound");
    }
}
