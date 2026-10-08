using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Admin;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using static Jobbliggaren.Api.IntegrationTests.Feedback.FeedbackKit;

namespace Jobbliggaren.Api.IntegrationTests.Feedback;

[Collection("Api")]
public sealed class FeedbackScreenshotTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string ImagePath(Guid id) => $"{DetailPath(id)}/screenshot";

    private static async Task<byte[]> PngAsync(byte red = 20)
    {
        using var image = new Image<Rgba32>(2, 3, new Rgba32(red, 40, 60, 255));
        using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder(), Ct);
        return stream.ToArray();
    }

    private static void AddImage(MultipartFormDataContent form, byte[] png, string name = "screenshot")
    {
        var image = new ByteArrayContent(png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, name, "synthetic-screenshot.png");
    }

    private static async Task<HttpResponseMessage> SubmitImageAsync(
        HttpClient client, object payload, byte[] png, CancellationToken ct)
    {
        using var form = Form(payload);
        AddImage(form, png);
        return await client.PostAsync(SubmitPath, form, ct);
    }

    private async Task<IReadOnlyList<FeedbackScreenshot>> ImagesAsync(JobSeekerId owner)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAppDbContext>().FeedbackScreenshots
            .AsNoTracking().Where(s => s.JobSeekerId == owner).ToListAsync(Ct);
    }

    private static void AssertImageHeaders(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task Submit_AndAdminRead_StoreOneNormalizedImageAndExposeOnlyItsMetadataInTheDetail()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var admin = (await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct)).Client;
        using var form = Form(Payload(Guid.NewGuid(), "jobs", rating: 4));
        var input = new ByteArrayContent(await PngAsync());
        input.Headers.ContentType = new MediaTypeHeaderValue("image/svg+xml");
        form.Add(input, "screenshot", "synthetic-untrusted.svg");

        using var submitted = await reporter.Client.PostAsync(SubmitPath, form, Ct);

        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (id, replayed) = await SubmittedAsync(submitted, Ct);
        replayed.ShouldBeFalse();
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
        var stored = (await ImagesAsync(reporter.JobSeekerId)).ShouldHaveSingleItem();
        stored.SubmissionId.Value.ShouldBe(id);
        stored.JobSeekerId.ShouldBe(reporter.JobSeekerId);
        stored.Width.ShouldBe(2);
        stored.Height.ShouldBe(3);
        using var image = await admin.GetAsync(ImagePath(id), Ct);
        image.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertImageHeaders(image);
        image.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("image/png");
        (await image.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(stored.Content.ToArray());
        using var detailResponse = await admin.GetAsync(DetailPath(id), Ct);
        var detail = await JsonAsync(detailResponse, Ct);
        var metadata = detail.GetProperty("screenshot");
        metadata.GetProperty("width").GetInt32().ShouldBe(2);
        metadata.GetProperty("height").GetInt32().ShouldBe(3);
        metadata.EnumerateObject().Select(p => p.Name).ShouldBe(["width", "height"], ignoreOrder: true);
        detail.GetRawText().ShouldNotContain(Convert.ToBase64String(stored.Content.Span));
    }

    [Fact]
    public async Task AdminRead_AnImage_Is401Or403UnlessTheAccountIsAnAdmin()
    {
        var reporter = await ReporterAsync(factory, Ct);
        using var submitted = await SubmitImageAsync(reporter.Client,
            Payload(Guid.NewGuid(), "jobs", rating: 4), await PngAsync(), Ct);
        var (id, _) = await SubmittedAsync(submitted, Ct);
        using var anonymous = await factory.CreateClient().GetAsync(ImagePath(id), Ct);
        using var forbidden = await reporter.Client.GetAsync(ImagePath(id), Ct);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        AssertImageHeaders(anonymous);
        AssertImageHeaders(forbidden);
        anonymous.Content.Headers.ContentType?.MediaType.ShouldNotBe("image/png");
        forbidden.Content.Headers.ContentType?.MediaType.ShouldNotBe("image/png");
    }

    [Fact]
    public async Task AdminRead_NoImageOrUnknownSubmission_Is404WithNoImageContentType()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var admin = (await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct)).Client;
        var id = await SubmitNewAsync(reporter.Client, "jobs", 4, null, Ct);

        foreach (var missingId in new[] { id, Guid.NewGuid() })
        {
            using var response = await admin.GetAsync(ImagePath(missingId), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            AssertImageHeaders(response);
            response.Content.Headers.ContentType?.MediaType.ShouldNotBe("image/png");
        }
    }

    [Fact]
    public async Task Operator_DeleteOnlyTheImage_PreservesFeedbackAndNoticeAndReportsActualImageAbsence()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var admin = (await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct)).Client;
        const string comment = "Synthetic operator image erasure case";
        using var submitted = await SubmitImageAsync(reporter.Client,
            Payload(Guid.NewGuid(), "jobs", rating: 4, comment: comment), await PngAsync(), Ct);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (id, _) = await SubmittedAsync(submitted, Ct);
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
        (await ImagesAsync(reporter.JobSeekerId)).ShouldHaveSingleItem().SubmissionId.Value.ShouldBe(id);

        // External actor: operator commands from docs/runbooks/recruiter-pii-erasure.md.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var erased = await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM public.feedback_screenshots WHERE submission_id = @submission_id;",
                [new NpgsqlParameter("submission_id", NpgsqlDbType.Uuid) { Value = id }], Ct);
            erased.ShouldBe(1);
        }

        using var image = await admin.GetAsync(ImagePath(id), Ct);
        image.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertImageHeaders(image);
        image.Content.Headers.ContentType?.MediaType.ShouldNotBe("image/png");
        using var detailResponse = await admin.GetAsync(DetailPath(id), Ct);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await JsonAsync(detailResponse, Ct);
        detail.GetProperty("screenshot").ValueKind.ShouldBe(JsonValueKind.Null);
        detail.GetProperty("rating").GetInt32().ShouldBe(4);
        detail.GetProperty("comment").GetString().ShouldBe(comment);
        detail.GetProperty("notification").ValueKind.ShouldBe(JsonValueKind.Object);
        (await ImagesAsync(reporter.JobSeekerId)).ShouldBeEmpty();
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
    }

    [Fact]
    public async Task Operator_DeleteWholeSubmissionInOneTransaction_RemovesImageFeedbackAndNoticeButKeepsSuppression()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var admin = (await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct)).Client;
        using var submitted = await SubmitImageAsync(reporter.Client,
            Payload(Guid.NewGuid(), "jobs", rating: 4), await PngAsync(), Ct);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (id, _) = await SubmittedAsync(submitted, Ct);
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
        (await ImagesAsync(reporter.JobSeekerId)).ShouldHaveSingleItem().SubmissionId.Value.ShouldBe(id);

        // External actor: operator commands from docs/runbooks/recruiter-pii-erasure.md.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            (await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM public.feedback_screenshots WHERE submission_id = @submission_id;",
                [new NpgsqlParameter("submission_id", NpgsqlDbType.Uuid) { Value = id }], Ct)).ShouldBe(1);
            (await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM public.feedback_notifications WHERE submission_id = @submission_id;",
                [new NpgsqlParameter("submission_id", NpgsqlDbType.Uuid) { Value = id }], Ct)).ShouldBe(1);
            (await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM public.feedback_submissions WHERE id = @submission_id;",
                [new NpgsqlParameter("submission_id", NpgsqlDbType.Uuid) { Value = id }], Ct)).ShouldBe(1);
            await transaction.CommitAsync(Ct);
        }

        using var detail = await admin.GetAsync(DetailPath(id), Ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var image = await admin.GetAsync(ImagePath(id), Ct);
        image.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        AssertImageHeaders(image);
        (await ImagesAsync(reporter.JobSeekerId)).ShouldBeEmpty();
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 1));
    }

    [Theory]
    [InlineData("extra-field")]
    [InlineData("extra-file")]
    [InlineData("duplicate-payload")]
    [InlineData("duplicate-screenshot")]
    [InlineData("missing-payload")]
    [InlineData("screenshot-field")]
    public async Task Submit_UnknownDuplicateOrMissingParts_Are400AndStoreNothing(string shape)
    {
        var reporter = await ReporterAsync(factory, Ct);
        var payload = Payload(Guid.NewGuid(), "jobs", rating: 4);
        using var form = shape == "missing-payload" ? new MultipartFormDataContent() : Form(payload);
        var png = await PngAsync();
        AddImage(form, png);
        switch (shape)
        {
            case "extra-field": form.Add(new StringContent("synthetic"), "unknown"); break;
            case "extra-file": AddImage(form, png, "unknown"); break;
            case "duplicate-payload":
                form.Add(new StringContent(System.Text.Json.JsonSerializer.Serialize(payload)), "payload"); break;
            case "duplicate-screenshot": AddImage(form, png); break;
            case "screenshot-field": form.Add(new StringContent("synthetic"), "screenshot"); break;
        }

        using var response = await reporter.Client.PostAsync(SubmitPath, form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
        (await ImagesAsync(reporter.JobSeekerId)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_ExternalClientShapesAPayloadOverSixtyFourKibibytes_RejectsItBeforeStoringAnyRows(
        bool rawMultibytePadding)
    {
        var reporter = await ReporterAsync(factory, Ct);
        var key = Guid.NewGuid();
        var validPayload = JsonSerializer.Serialize(Payload(key, "jobs", rating: 4), JsonSerializerOptions.Web);
        var padding = rawMultibytePadding ? new string('å', 40_000) : new string('x', 65_537);
        // External actor: a multipart client adds ignored JSON padding, with raw UTF-8 rather than escaped characters.
        var json = validPayload[..^1] + ",\"padding\":\"" + padding + "\"}";
        Encoding.UTF8.GetByteCount(json).ShouldBeGreaterThan(64 * 1024);
        if (rawMultibytePadding)
        {
            json.Length.ShouldBeLessThan(64 * 1024);
            json.ShouldContain("å");
            json.ShouldNotContain("\\u00e5");
        }
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("submissionKey").GetGuid().ShouldBe(key);
        document.RootElement.GetProperty("rating").GetInt32().ShouldBe(4);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(json, Encoding.UTF8, "application/json"), "payload" },
        };
        AddImage(form, await PngAsync());

        using var response = await reporter.Client.PostAsync(SubmitPath, form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
        (await ImagesAsync(reporter.JobSeekerId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Submit_AnImageWithoutRatingOrComment_Is400AndStoresNothing()
    {
        var reporter = await ReporterAsync(factory, Ct);
        using var response = await SubmitImageAsync(reporter.Client,
            Payload(Guid.NewGuid(), "jobs"), await PngAsync(), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ProblemTitleAsync(response, Ct)).ShouldBe("Feedback.Empty");
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
        (await ImagesAsync(reporter.JobSeekerId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Submit_AFileOverFiveMebibytes_Is400AndStoresNothing()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var oversized = new byte[5 * 1024 * 1024 + 1];
        (await PngAsync()).CopyTo(oversized, 0);
        using var response = await SubmitImageAsync(reporter.Client,
            Payload(Guid.NewGuid(), "jobs", rating: 4), oversized, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
        (await ImagesAsync(reporter.JobSeekerId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Submit_AKeyThatAlreadyHasAnImage_PreservesTheFirstImageOnReplay()
    {
        var reporter = await ReporterAsync(factory, Ct);
        var key = Guid.NewGuid();
        using var first = await SubmitImageAsync(reporter.Client, Payload(key, "jobs", rating: 4), await PngAsync(), Ct);
        var (id, _) = await SubmittedAsync(first, Ct);
        var before = (await ImagesAsync(reporter.JobSeekerId)).ShouldHaveSingleItem();
        using var changed = await SubmitImageAsync(reporter.Client,
            Payload(key, "jobs", rating: 1), await PngAsync(240), Ct);
        using var removed = await SubmitAsync(reporter.Client, Payload(key, "jobs", rating: 1), Ct);
        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SubmittedAsync(changed, Ct)).ShouldBe((id, true));
        (await SubmittedAsync(removed, Ct)).ShouldBe((id, true));
        var after = (await ImagesAsync(reporter.JobSeekerId)).ShouldHaveSingleItem();
        after.Id.ShouldBe(before.Id);
        after.Content.ToArray().ShouldBe(before.Content.ToArray());
        (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(new Stored(1, 1, 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submit_ARealUniqueConflict_KeepsOnlyTheImagesBelongingToCommittedSubmissions(bool sameKey)
    {
        var reporter = await ReporterAsync(factory, Ct);
        var heldKey = Guid.NewGuid();
        var winnerKey = sameKey ? heldKey : Guid.NewGuid();
        HttpResponseMessage? competitor = null;
        var otherPng = await PngAsync(240);
        factory.FeedbackSaveRace.Arm(reporter.JobSeekerId, async raceCt =>
            competitor = await SubmitImageAsync(reporter.Client,
                Payload(winnerKey, "jobs", rating: 5), otherPng, raceCt));
        HttpResponseMessage held;
        try
        {
            held = await SubmitImageAsync(reporter.Client,
                Payload(heldKey, "jobs", rating: 1), await PngAsync(), Ct);
        }
        finally { factory.FeedbackSaveRace.Disarm(); }
        using (held)
        using (competitor)
        {
            factory.FeedbackSaveRace.Fired.ShouldBe(1);
            competitor.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Created);
            held.StatusCode.ShouldBe(sameKey ? HttpStatusCode.OK : HttpStatusCode.Created);
            var winner = await SubmittedAsync(competitor, Ct);
            var answer = await SubmittedAsync(held, Ct);
            answer.Replayed.ShouldBe(sameKey);
            if (sameKey) answer.Id.ShouldBe(winner.Id);
            else answer.Id.ShouldNotBe(winner.Id);
            var images = await ImagesAsync(reporter.JobSeekerId);
            images.Count.ShouldBe(sameKey ? 1 : 2);
            using var decodedWinner = Image.Load<Rgba32>(images.Single(s => s.SubmissionId.Value == winner.Id).Content.Span);
            decodedWinner[0, 0].R.ShouldBe((byte)240);
            (await StoredAsync(factory, reporter.JobSeekerId, Ct)).ShouldBe(
                sameKey ? new Stored(1, 1, 1) : new Stored(2, 2, 1));
        }
    }

    [Fact]
    public async Task Submit_TwoOwnersAlreadyOccupyIngress_RejectsAThirdOwnerWithoutQueueing()
    {
        var first = await ReporterAsync(factory, Ct);
        var second = await ReporterAsync(factory, Ct);
        var third = await ReporterAsync(factory, Ct);
        var bothHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HttpResponseMessage? competitor = null;
        factory.FeedbackSaveRace.Arm(first.JobSeekerId, async firstCt =>
        {
            factory.FeedbackSaveRace.Arm(second.JobSeekerId, async secondCt =>
            {
                bothHeld.SetResult();
                await release.Task.WaitAsync(secondCt);
            });
            competitor = await SubmitAsync(second.Client, Payload(Guid.NewGuid(), "jobs", rating: 4), firstCt);
        });
        var pending = SubmitAsync(first.Client, Payload(Guid.NewGuid(), "jobs", rating: 4), Ct);
        try
        {
            await bothHeld.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            using var rejected = await SubmitAsync(third.Client, Payload(Guid.NewGuid(), "jobs", rating: 4), Ct);
            rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            (await StoredAsync(factory, third.JobSeekerId, Ct)).ShouldBe(new Stored(0, 0, 0));
        }
        finally
        {
            release.TrySetResult();
            try
            {
                using var completed = await pending;
                completed.StatusCode.ShouldBe(HttpStatusCode.Created);
                competitor.ShouldNotBeNull().StatusCode.ShouldBe(HttpStatusCode.Created);
            }
            finally
            {
                factory.FeedbackSaveRace.Disarm();
                competitor?.Dispose();
            }
        }
    }
}
