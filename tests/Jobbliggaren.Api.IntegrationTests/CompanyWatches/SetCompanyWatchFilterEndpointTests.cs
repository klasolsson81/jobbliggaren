using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// Bevakning F4a (#803) — HTTP + relational wiring for
/// <c>PUT /api/v1/me/company-watches/{id}/filter</c> against Testcontainers Postgres. Proves the
/// things a unit test cannot: the command actually reaches the nullable <c>jsonb</c> column in BOTH
/// directions (a set writes both axes; an all-empty body clears the column to SQL NULL — not to an
/// empty object, which would be a second, non-canonical "no filter" representation), the audit row is
/// written exactly once, and the cross-user attempt is a 404 rather than a 403 (BC-6: a 403 would
/// confirm the watch id exists and turn the endpoint into an enumeration oracle).
/// </summary>
[Collection("Api")]
public class SetCompanyWatchFilterEndpointTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches";

    // Hoisted (CA1861): these travel as request-body arrays on every call. The two geo lists are
    // DISJOINT JobTech namespaces — a kommun id is never a län id.
    private static readonly string[] Kommun = ["gbg_kn"];
    private static readonly string[] OtherKommun = ["kommun_x"];
    private static readonly string[] Lan = ["skane_lan"];
    private static readonly string[] MalformedLan = ["inte giltig"];
    private static readonly string[] NoIds = [];

    private readonly ApiFactory _factory = factory;

    private async Task<HttpClient> AuthenticatedClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"cw-filter-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    // A fresh legal-entity org.nr per follow keeps the active-partial UNIQUE(user_id, org.nr) clear.
    private static string NewOrgNr() => $"55{Random.Shared.Next(10000000, 99999999)}";

    private static async Task<Guid> FollowAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(
            Endpoint, new { organizationNumber = NewOrgNr() }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return Guid.Parse(json.GetProperty("id").GetString()!);
    }

    private static Task<HttpResponseMessage> PutFilterAsync(
        HttpClient client, Guid watchId, object body, CancellationToken ct) =>
        client.PutAsJsonAsync($"{Endpoint}/{watchId}/filter", body, ct);

    // Reads the filter column as RAW text — the on-disk form, bypassing the EF converter. Null when
    // the column IS SQL NULL (the canonical "no filter").
    private async Task<string?> ReadRawFilterAsync(Guid watchId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT filter::text FROM company_watches WHERE id = @id";
        cmd.Parameters.AddWithValue("id", watchId);
        var raw = await cmd.ExecuteScalarAsync(ct);
        await conn.CloseAsync();
        return raw is null or DBNull ? null : (string)raw;
    }

    private async Task<WatchFilterSpec?> ReadFilterAsync(Guid watchId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = new CompanyWatchId(watchId);
        var watch = await db.CompanyWatches.AsNoTracking().IgnoreQueryFilters()
            .SingleAsync(w => w.Id == id, ct);
        return watch.Filter;
    }

    private async Task<int> CountFilterChangedAuditRowsAsync(Guid watchId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.AsNoTracking()
            .CountAsync(a => a.EventType == "CompanyWatch.FilterChanged" && a.AggregateId == watchId, ct);
    }

    // ── Auth ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_filter_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await PutFilterAsync(
            client, Guid.NewGuid(), new { municipalities = Kommun, onlyMatched = false }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ── Round-trip through jsonb (both axes) ─────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_filter_persists_both_geo_axes_to_jsonb()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(client, ct);

        var response = await PutFilterAsync(
            client,
            watchId,
            new
            {
                municipalities = Kommun,
                regions = Lan,
                onlyMatched = true,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Read the ROW, not the response — the endpoint returns 204, so the only honest proof that the
        // write landed is the column itself.
        var stored = await ReadFilterAsync(watchId, ct);
        stored.ShouldNotBeNull();
        stored!.Municipalities.ShouldBe(["gbg_kn"]);
        stored.Regions.ShouldBe(["skane_lan"],
            "län-axeln måste nå kolumnen hela vägen genom transport → command → VO → jsonb");
        stored.OnlyMatched.ShouldBeTrue();

        var raw = await ReadRawFilterAsync(watchId, ct);
        raw.ShouldNotBeNull();
        raw!.ShouldContain("skane_lan");
    }

    [Fact]
    public async Task PUT_filter_with_null_lists_on_the_wire_is_treated_as_empty()
    {
        // The wire tolerates omitted lists (a client that never touched an axis sends nothing), but a
        // null list must never reach the domain — it normalises to empty. Here only OnlyMatched is set,
        // so the result is a legal, geo-free spec rather than a 400 or a NullReference.
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(client, ct);

        var response = await PutFilterAsync(client, watchId, new { onlyMatched = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await ReadFilterAsync(watchId, ct);
        stored.ShouldNotBeNull();
        stored!.Municipalities.ShouldBeEmpty();
        stored.Regions.ShouldBeEmpty();
        stored.OnlyMatched.ShouldBeTrue();
    }

    [Fact]
    public async Task PUT_filter_replaces_previous_selection_wholesale()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(client, ct);

        (await PutFilterAsync(
            client, watchId,
            new { municipalities = Kommun, regions = NoIds, onlyMatched = true },
            ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The user deselects the kommun and picks a län instead — a full replace, not a merge.
        (await PutFilterAsync(
            client, watchId,
            new { municipalities = NoIds, regions = Lan, onlyMatched = false },
            ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var stored = await ReadFilterAsync(watchId, ct);
        stored.ShouldNotBeNull();
        stored!.Municipalities.ShouldBeEmpty("den avvalda kommunen ska vara borta, inte sammanslagen");
        stored.Regions.ShouldBe(["skane_lan"]);
        stored.OnlyMatched.ShouldBeFalse();
    }

    // ── The clear path — an empty body clears the column to SQL NULL ─────────────────────────────

    [Fact]
    public async Task PUT_filter_with_empty_selection_clears_column_to_sql_null()
    {
        // The canonical "no filter" is a NULL column — never an empty jsonb object. Two representations
        // of "no filter" would mean every reader has to know both, and the empty-spec invariant exists
        // precisely to keep that from happening.
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(client, ct);
        (await PutFilterAsync(
            client, watchId,
            new { municipalities = Kommun, regions = Lan, onlyMatched = true },
            ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadRawFilterAsync(watchId, ct)).ShouldNotBeNull("förutsättning: filtret är satt");

        var response = await PutFilterAsync(
            client, watchId,
            new
            {
                municipalities = NoIds,
                regions = NoIds,
                onlyMatched = false,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadRawFilterAsync(watchId, ct)).ShouldBeNull(
            "ett tomt val rensar kolumnen till SQL NULL — annars kan användaren aldrig stänga av filtret");
        (await ReadFilterAsync(watchId, ct)).ShouldBeNull();
    }

    // ── Audit (Art. 5(2)/30) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_filter_writes_exactly_one_filter_changed_audit_row()
    {
        // The ort axis SUPPRESSES hit creation (8A), so this write changes the scope of downstream
        // personal-data processing — it is auditable regardless of consent. ONE row per write (a
        // double-write would corrupt the accountability trail's count).
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(client, ct);

        var response = await PutFilterAsync(
            client, watchId, new { municipalities = Kommun, onlyMatched = false }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CountFilterChangedAuditRowsAsync(watchId, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task PUT_filter_clear_is_audited_with_the_same_event_type()
    {
        // ONE event type for both directions (set and clear): the direction is recoverable from the
        // stored filter, and audit_log has no payload column — encoding values into event NAMES is a
        // trap. Two writes → two rows.
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(client, ct);

        await PutFilterAsync(
            client, watchId, new { municipalities = Kommun, onlyMatched = false }, ct);
        await PutFilterAsync(
            client, watchId,
            new { municipalities = NoIds, regions = NoIds, onlyMatched = false },
            ct);

        (await CountFilterChangedAuditRowsAsync(watchId, ct)).ShouldBe(2,
            "både set och clear är auditerade händelser på samma event_type");
    }

    // ── Cross-user isolation (BC-6) ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_filter_on_another_users_watch_returns_404_not_403()
    {
        // BC-6 IDOR pin. A 403 (or a 400) would tell the attacker the id EXISTS — the answer must be
        // indistinguishable from the unknown-id answer.
        var ct = TestContext.Current.CancellationToken;
        var owner = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(owner, ct);
        var attacker = await AuthenticatedClientAsync(ct);

        var response = await PutFilterAsync(
            attacker, watchId, new { municipalities = OtherKommun, onlyMatched = true }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        (await ReadFilterAsync(watchId, ct)).ShouldBeNull(
            "ägarens bevakning ska vara orörd efter angriparens försök");
        (await CountFilterChangedAuditRowsAsync(watchId, ct)).ShouldBe(0,
            "ett misslyckat försök muterar inget och ska inte lämna en success-audit-rad");
    }

    [Fact]
    public async Task PUT_filter_on_unknown_watch_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);

        var response = await PutFilterAsync(
            client, Guid.NewGuid(), new { municipalities = Kommun, onlyMatched = false }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Transport guard ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PUT_filter_with_invalid_region_concept_id_returns_400()
    {
        // The VO's default-deny format rule reaches the wire as a 400 (Validation → 400 via the central
        // DomainError mapper), not a 500.
        var ct = TestContext.Current.CancellationToken;
        var client = await AuthenticatedClientAsync(ct);
        var watchId = await FollowAsync(client, ct);

        var response = await PutFilterAsync(
            client, watchId, new { regions = MalformedLan, onlyMatched = false }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadFilterAsync(watchId, ct)).ShouldBeNull();
    }
}
