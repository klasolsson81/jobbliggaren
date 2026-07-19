using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

/// <summary>
/// #664 — the admin-gated enqueue endpoint for the one-off source_file_name mask backfill. Proves the
/// Admin authorization gate (401 anon / 403 non-admin) on the new route. The enqueue path itself is not
/// integration-tested here: the endpoint enqueues via the REAL <c>IBackgroundJobClient</c> (parity
/// #544's <c>backfill-orgnr-token</c>, which is likewise not endpoint-tested), and the Api test host has
/// no Hangfire schema (<c>hangfire.job</c> does not exist → an enqueue would 42P01). The job's behavior
/// — masking, dry-run, soft-deleted coverage, idempotency — is proven against real Postgres in
/// <c>BackfillParsedResumeSourceFileNameMaskJobIntegrationTests</c>; the dryRun=true default and the
/// 202 response are a three-line pass-through verified by the endpoint source.
/// </summary>
[Collection("Api")]
public class AdminBackfillSourceFileNameMaskTests(ApiFactory factory)
{
    private const string Path = "/api/v1/admin/resumes/backfill-source-filename-mask";

    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.PostAsync(Path, content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_admin_user_returns_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var response = await client.PostAsync(Path, content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
