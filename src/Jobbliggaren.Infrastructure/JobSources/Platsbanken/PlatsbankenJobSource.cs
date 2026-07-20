using System.Runtime.CompilerServices;
using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.JobSources.Platsbanken;

/// <summary>
/// Platsbanken-implementation av <see cref="IJobSource"/>. Konsumerar
/// <see cref="IJobTechStreamClient"/> + <see cref="JobTechPayloadSanitizer"/>
/// och översätter JobTech-shape till <see cref="JobAdImportItem"/>-DTOs som
/// Application-handlers kan konsumera utan att exponeras för wire-format
/// eller osanerad PII.
/// </summary>
internal sealed partial class PlatsbankenJobSource(
    IJobTechStreamClient streamClient,
    IJobTechSearchClient searchClient,
    IDateTimeProvider clock,
    ILogger<PlatsbankenJobSource> logger) : IJobSource
{
    public JobSource Source => JobSource.Platsbanken;

    // Bounded retry av snapshot-fetch vid mid-stream-trunkering. JobTechs
    // /v2/snapshot (~300 MB+ singel-GET, parameterlös, ingen resume —
    // web-verifierat 2026-05-16) termineras icke-deterministiskt mitt i
    // strömmen (Batch 0 CloudWatch-evidens: trunkering vid 21–364 MB,
    // 87–442 s). Eftersom droppen är icke-deterministisk kan ett senare
    // försök leverera mer/hela; 3 försök kapar kostnaden (~3 min vid
    // JobTech 1 req/min) — resten fylls av hybrid stream-katch-up + nästa
    // cron (CTO A2→hybrid 2026-05-16, ADR 0032-amendment). Redan-yieldade
    // items är idempotenta dubbletter via UNIQUE-index (ADR 0032 §5).
    private const int MaxSnapshotAttempts = 3;

    public async IAsyncEnumerable<JobAdImportItem> FetchSnapshotAsync(
        SnapshotOutcomeRecorder outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        LogSnapshotStarted(logger);

        // #551 — harvest AF's remote/distans classification ONCE per snapshot run (D1: snapshot owns
        // remote; the 10-min stream does not). A null result (fetch failed/absent) is the fail-safe
        // signal: MapFacets then emits JobAdFacets.Remote = null (preserve) so a bad harvest can never
        // flip the corpus to false. A successful harvest assigns explicit true/false per set membership,
        // which also handles AF declassifying an ad (dropped from the set → set back to false).
        var remoteIds = await TryFetchRemoteIdSetAsync(cancellationToken);

        for (var attempt = 1; attempt <= MaxSnapshotAttempts; attempt++)
        {
            // #510 — reset PER ATTEMPT: every retry re-streams from element 0, so
            // counters held across attempts double-counted the parsed prefix. A
            // truncate-then-succeed run then recorded ParsedTotal as the cross-attempt
            // SUM, which inflated the 7-day MAX(ParsedTotal)-baseline and tripped the
            // relative floor for every healthy run after it → miss-tracking (stale-ad
            // archiving) suppressed for up to 7 days. ParsedTotal is defined as the
            // FINAL attempt's element count; yields still span attempts (idempotent
            // duplicates via UNIQUE index, ADR 0032 §5).
            var converted = 0;
            var total = 0;
            // Färsk GET per försök — strömmar per hit, ~300 MB materialiseras
            // aldrig (streaming-fix 2026-05-16). Konsumenten
            // (SyncPlatsbankenSnapshotJob) kör child-scope per yieldat item.
            await using var enumerator = streamClient
                .FetchSnapshotAsync(cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            var truncated = false;

            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw; // Cancellation propagerar alltid — aldrig svald.
                }
                catch (Exception ex)
                    when (ex is JsonException or IOException or HttpRequestException)
                {
                    // ROTORSAKS-FIX (Batch 0 2026-05-16): mid-stream-trunkering
                    // fångas vid ENUMERATION-boundary. Detta är skilt från
                    // per-item-upsert-catchen i SyncPlatsbankenSnapshotJob —
                    // slå ALDRIG ihop dem (ADR 0032 §5-clarification: ofångad
                    // enumeration var hela storm-mekanismen, 60 starts/0
                    // completes). Ofångad här = Hangfire.AutomaticRetry-loop.
                    truncated = true;
                    if (attempt < MaxSnapshotAttempts)
                        LogSnapshotTruncatedRetrying(logger, ex, attempt, total);
                    else
                        LogSnapshotTruncatedGivingUp(logger, ex, attempt, converted, total);
                    break;
                }

                if (!moved)
                {
                    // Strömmen slut utan trunkering = fullständig snapshot.
                    // ADR 0032-amendment 2026-05-23: registrera utfall för caller
                    // INNAN yield break så miss-tracking kan köra säkert.
                    LogSnapshotCompleted(logger, converted, total);
                    outcome.Record(new SnapshotOutcome(
                        ParsedTotal: total,
                        Attempts: attempt,
                        TruncatedAndExhausted: false));
                    yield break;
                }

                var hit = enumerator.Current;
                total++;

                if (hit.Removed == true)
                    continue; // Snapshot innehåller bara aktiva; defensive skip.

                var item = TryConvertToImportItem(hit, remoteIds);
                if (item is null)
                    continue;

                converted++;
                yield return item;
            }

            if (truncated && attempt >= MaxSnapshotAttempts)
            {
                // Bounded retry uttömd → avsluta GRACEFULLT (ingen ofångad
                // exception → ingen retry-storm). Parsad prefix är redan
                // idempotent persisterad; hybrid stream-katch-up + nästa
                // cron fyller resten (CTO 2026-05-16).
                // ADR 0032-amendment 2026-05-23: registrera trunkerings-utfall
                // → caller skippar miss-tracking (kan inte särskilja missing
                // från trunkering).
                LogSnapshotCompleted(logger, converted, total);
                outcome.Record(new SnapshotOutcome(
                    ParsedTotal: total,
                    Attempts: attempt,
                    TruncatedAndExhausted: true));
                yield break;
            }
            // truncated && attempt < Max → ny iteration = färsk GET.
        }
    }

    public async IAsyncEnumerable<JobAdChange> StreamChangesAsync(
        DateTimeOffset since,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // #483 Low (CTO F2 P-ACL + B-GRACE) — mid-stream transport truncation of /v2/stream is
        // caught HERE, at the enumeration boundary, symmetric with FetchSnapshotAsync above
        // (which owns the same JsonException/IOException/HttpRequestException class). An uncaught
        // throw would propagate out of SyncPlatsbankenStreamJob's foreach to Hangfire
        // AutomaticRetry — the exact storm mechanism ADR 0032 §5 built the snapshot path to
        // avoid (uncaught enumeration = "hela storm-mekanismen", 60 starts/0 completes).
        //
        // Unlike snapshot, the stream does NOT retry: it is INCREMENTAL and self-healing. The
        // 10-min cron's 15-min overlap window + the nightly snapshot + idempotent UNIQUE-index
        // upserts already re-deliver a dropped tail ("Tappade kör tolereras", Fowler 2002
        // Idempotent Receiver — see SyncPlatsbankenStreamJob). Snapshot retries because it is
        // AUTHORITATIVE and feeds miss-tracking (must become complete before recording the
        // outcome); the stream has no such consumer, so a bounded refetch would just re-stream
        // the same window the overlap already covers — a second normaliser for one failure. So on
        // truncation we log and complete gracefully.
        //
        // DELIBERATELY NOT a shared helper with FetchSnapshotAsync: the two share the
        // manual-enumerator SKELETON but differ in retry/outcome semantics — extract only at a
        // third consumer (§3.6 rule-of-three). The manual GetAsyncEnumerator + try-around-
        // MoveNextAsync (never around a yield — C# forbids yield inside a try with a catch) is
        // exactly why FetchSnapshotAsync drives its enumerator by hand too.
        await using var enumerator = streamClient
            .StreamChangesAsync(since, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool moved;
            try
            {
                moved = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException)
            {
                throw; // Cancellation always propagates — never swallowed as truncation.
            }
            catch (Exception ex)
                when (ex is JsonException or IOException or HttpRequestException)
            {
                // Mid-stream transport truncation. NEVER widen to catch(Exception) or fold in the
                // per-item conversion below — data errors (schema drift) are already skipped
                // per-element inside JobTechStreamClient (see its rad 87 warning: keep the two
                // error classes apart).
                LogStreamTruncated(logger, ex);
                yield break; // Grace: overlap window + nightly snapshot fill the dropped tail.
            }

            if (!moved)
                yield break;

            var hit = enumerator.Current;

            if (string.IsNullOrWhiteSpace(hit.Id))
                continue;

            // Npgsql timestamptz kräver Offset=0 — normalisera externa JobTech-
            // datum till UTC vid ACL-boundary (instant bevaras). clock.UtcNow är
            // redan UTC (no-op). Se TryConvertToImportItem för fullt motiv.
            var occurredAt = (hit.LastPublicationDate
                ?? hit.RemovedDate
                ?? hit.PublicationDate
                ?? clock.UtcNow).ToUniversalTime();

            if (hit.Removed == true)
            {
                yield return new JobAdRemoval(hit.Id, occurredAt);
                continue;
            }

            var item = TryConvertToImportItem(hit);
            if (item is null)
                continue;

            yield return new JobAdUpsert(hit.Id, item, occurredAt);
        }
    }

    public async Task<JobAdImportItem?> RefetchByExternalIdAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);

        JobTechHit? hit;
        try
        {
            hit = await searchClient.GetAdByIdAsync(externalId, cancellationToken);
        }
        catch (Refit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // ADR 0032-amendment 2026-05-23 — 404 = "annons borttagen från
            // källan", caller hanterar som skip+count, inte arkivering.
            // Retention-disciplinen ägs av snapshot-flödets miss-tracking,
            // inte per-ID-fetch (architect-rond 2026-05-24).
            LogRefetchNotFound(logger, externalId);
            return null;
        }

        if (hit is null)
        {
            // Refit 8+ nullable-return-shape: 404 → null utan exception.
            LogRefetchNotFound(logger, externalId);
            return null;
        }

        return TryConvertToImportItem(hit);
    }

    // #551 — remoteIds is the snapshot harvest's set (null on the stream/refetch paths and on a failed
    // harvest). Threaded to MapFacets, where null → JobAdFacets.Remote = null (preserve; the aggregate
    // keeps its current remote value), non-null → explicit membership true/false.
    private JobAdImportItem? TryConvertToImportItem(JobTechHit hit, IReadOnlySet<string>? remoteIds = null)
    {
        if (string.IsNullOrWhiteSpace(hit.Id) || hit.PublicationDate is null)
            return null;

        // SECURITY-NOTE (#842 Tier A, ADR 0106) — NOTHING HERE SCRUBS THE AD BODY, deliberately.
        // The scrub is a JobAd AGGREGATE INVARIANT (JobAd.Import/UpdateFromSource →
        // ApplyContactRedaction): placement inside the funnel is what makes it durable (F-A — the
        // nightly sync re-applies it on every rewrite) and complete (F-B — the extractor reads the
        // aggregate's post-scrub text). An ACL-side scrub would be a second normalizer the next
        // write path forgets. What the ACL DOES own is the wire shape: the declared
        // application_contacts are mapped to Domain AdContacts below and travel with the payload.
        // The recruiter's NAME in free text remains, is FTS-searchable, and is Tier B's population
        // (whole-record erasure) — that residual is disclosed, not hidden (D12).
        var headline = hit.Headline?.Trim();
        var description = hit.Description?.Text?.Trim();
        // sec-Min-1: filtrera bort mailto:-länkar (application_details.url-fallback
        // kan vara `mailto:rekryterare@acme.se?subject=Job` — det är PII vi inte vill
        // persistera i job_ads.url-kolumnen).
        //
        // v2-prioritering: webpage_url är top-level i v2 (web-verifierat 2026-05-13).
        // source_links är v1-fallback om legacy JobTech återaktiveras. application_details.url
        // är sista fallback (sällan satt — kan vara mailto).
        var url = FirstNonMailtoUrl(hit.WebpageUrl, hit.SourceLinks, hit.ApplicationDetails?.Url);
        var company = hit.Employer?.Name?.Trim();
        // Npgsql timestamptz kräver Offset=0 — normalisera externa JobTech-datum
        // till UTC vid ACL-boundary (instant bevaras). System.Text.Json tilldelar
        // lokal maskin-offset för JobTech-datum, vilket failar på icke-UTC-värdar
        // (funkade på Fargate=UTC, ej lokalt i Sverige=+02:00; ADR 0066-pivot).
        var publishedAt = hit.PublicationDate.Value.ToUniversalTime();
        var expiresAt = hit.LastPublicationDate?.ToUniversalTime();

        // Lite tolerant filtrering vid wire-format-luckor — JobAd.Import-faktorn
        // validerar slutligt (titel/desc/url non-empty + URL absolute).
        if (string.IsNullOrWhiteSpace(headline)
            || string.IsNullOrWhiteSpace(description)
            || string.IsNullOrWhiteSpace(url)
            || string.IsNullOrWhiteSpace(company))
        {
            LogHitSkipped(logger, hit.Id);
            return null;
        }

        // Serialisera hit till JSON och kör sanitizer. Sanitizer-allowlist
        // garanterar att raw_payload inte innehåller rekryterar-PII (TD-73 +
        // ADR 0032 §8-amendment).
        var rawJson = JsonSerializer.Serialize(hit);
        var sanitized = JobTechPayloadSanitizer.SanitizeForStorage(rawJson);

        return new JobAdImportItem(
            ExternalId: hit.Id,
            Title: headline,
            CompanyName: company,
            Description: description,
            Url: url,
            PublishedAt: publishedAt,
            ExpiresAt: expiresAt,
            SanitizedRawPayload: sanitized,
            Facets: MapFacets(hit, remoteIds),
            Requirements: MapRequirements(hit),
            DeclaredContacts: MapContacts(hit));
    }

    // #842 Tier A (re-bind R1(a)) — ACL translation of the declared contact persons. The wire's
    // role information lives in two keys: contact_type (a type code, usually null in live data)
    // and description (free text like "Rekryterare" — the form advertisers actually fill in);
    // contact_type wins when both exist, because it is the structured one. All-null entries are
    // wire junk and drop via AdContact.TryCreate (blank-is-absence, the JobAdFacets lesson).
    // Name is the ADVERTISER'S declaration here — never an inference (Origin = Declared; the
    // detector-promoted contacts are born in the aggregate with Origin = ExtractedFromBody and
    // Name = null).
    private static List<AdContact> MapContacts(JobTechHit hit)
    {
        if (hit.ApplicationContacts is not { Count: > 0 } wireContacts)
            return [];

        var contacts = new List<AdContact>(wireContacts.Count);
        foreach (var wire in wireContacts)
        {
            var contact = AdContact.TryCreate(
                wire.Name,
                role: string.IsNullOrWhiteSpace(wire.ContactType) ? wire.Description : wire.ContactType,
                wire.Email,
                wire.Telephone,
                AdContactOrigin.Declared);
            if (contact is not null)
                contacts.Add(contact);
        }

        return contacts;
    }

    // #841 — ACL translation of the seven source facets. Until 2026-07-13 these were derived by POSTGRES,
    // as STORED generated columns reading raw_payload — which meant the 30-day raw_payload purge recomputed
    // every one of them to NULL and filtered search, the matching engine and the company-watch scan silently
    // lost still-ACTIVE ads ~21.5 h/day. The values now come from here, are written in C# at the single
    // ingest funnel, and outlive the payload (ADR 0032 §8: "indefinitively för sanitized fields").
    //
    // THIS METHOD IS THE ONLY PLACE IN THE SYSTEM THAT KNOWS THE PAYLOAD'S SHAPE, and it must stay that
    // way — the JSON nesting and the naming gaps below are precisely the foreign-model knowledge an ACL
    // exists to absorb (Evans 2003 §14). Named arguments are mandatory: seven same-typed positional
    // strings would make a transposition a silently compiling bug.
    //
    // Two traps live here, both previously encoded in the SQL and both load-bearing:
    //   * occupation_group / employment_type / working_hours_type are TOP-LEVEL in the payload, while
    //     occupation (ssyk) and employer (org.nr) are NESTED. Reading ssyk from the top level yields a
    //     permanently NULL column with no compile error.
    //   * NAME GAP: the column/taxonomy type is worktime-extent (ADR 0067 Beslut 2) but the wire key is
    //     working_hours_type. Mapping WorktimeExtentConceptId from anything else yields silent always-NULL.
    // JobAdFacets normalises blank -> null, so a "" from the wire cannot enter the partial IS NOT NULL
    // indexes as a value nothing can ever match.
    //
    // #551 — remote is the eighth facet and the ONLY one not read from this hit's payload: the response
    // schema carries no per-ad remote field (ADR 0067 Beslut 3, amended 2026-07-18). Its value is AF's
    // `remote=true` classification, harvested separately as a set of ids (remoteIds). null set = "no
    // verdict this run" (stream/refetch, or a failed snapshot harvest) → JobAdFacets.Remote = null =
    // PRESERVE the aggregate's current value (never clobber). A non-null set = explicit membership.
    private static JobAdFacets MapFacets(JobTechHit hit, IReadOnlySet<string>? remoteIds) =>
        new(ssykConceptId: hit.Occupation?.ConceptId,
            occupationGroupConceptId: hit.OccupationGroup?.ConceptId,
            municipalityConceptId: hit.WorkplaceAddress?.MunicipalityConceptId,
            regionConceptId: hit.WorkplaceAddress?.RegionConceptId,
            employmentTypeConceptId: hit.EmploymentType?.ConceptId,
            worktimeExtentConceptId: hit.WorkingHoursType?.ConceptId,
            organizationNumber: hit.Employer?.OrganizationNumber,
            remote: remoteIds is null ? null : remoteIds.Contains(hit.Id));

    // #551 — the remote/distans harvest (D1 option i, ACL-internal). Paginates
    // jobsearch.api.jobtechdev.se/search?remote=true once per snapshot run and returns the set of
    // AF-classified remote source-ids. Kept BEHIND the IJobSource port (the ACL owns both the stream and
    // the search client, ADR 0032 §2) — Application never sees a second feed.
    //
    // FAIL-SAFE (D1, load-bearing): any failure returns null, NOT an empty set. MapFacets reads null as
    // "no verdict this run" → JobAdFacets.Remote = null → SetSourcePayload preserves the ad's current
    // value. An empty set would flip every ad to remote=false, silently un-remoting the corpus and
    // re-opening the #552 hole a remote ad exists to escape. So a JobSearch outage costs one night's
    // freshness, never correctness. Genuine cancellation still propagates.
    private async Task<IReadOnlySet<string>?> TryFetchRemoteIdSetAsync(CancellationToken cancellationToken)
    {
        const int PageSize = 100;
        // JobSearch caps offset at 2000; the remote total is well under it (~660, measured 2026-07-18).
        // If AF's remote corpus ever exceeds this, the cap bounds the set to the first 2000 — a partial
        // harvest is still fail-SAFE (the missing ads read false → #552-gated, never falsely-remote).
        const int MaxOffset = 2000;

        try
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var offset = 0;

            while (offset < MaxOffset)
            {
                var page = await searchClient.SearchRemoteAsync(offset, PageSize, cancellationToken);
                var hits = page.Hits;
                if (hits is null || hits.Count == 0)
                    break;

                foreach (var hit in hits)
                {
                    if (!string.IsNullOrWhiteSpace(hit.Id))
                        ids.Add(hit.Id);
                }

                offset += PageSize;
                if (offset >= (page.Total?.Value ?? 0))
                    break;
            }

            // A SUCCESSFUL but EMPTY response is the one edge the exception fail-safe does not cover: an
            // empty (non-null) set would flip the WHOLE corpus to remote=false — the same silent un-remoting
            // the fail-safe exists to prevent. AF's remote corpus is ~660; a sudden 0 is an anomaly (an
            // AF-side classification glitch), not a credible true signal. So treat it as a failed harvest →
            // null (preserve), never false (architect Note 1; documented in ADR 0067 amendment 2026-07-18).
            if (ids.Count == 0)
            {
                LogRemoteHarvestEmpty(logger);
                return null;
            }

            LogRemoteHarvestCompleted(logger, ids.Count);
            return ids;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Genuine cancellation propagates — never swallowed.
        }
        catch (Exception ex)
            when (ex is Refit.ApiException or HttpRequestException or TaskCanceledException
                or JsonException or IOException)
        {
            // Fail-safe: null (preserve), not an empty set (clobber). TaskCanceledException here is an
            // HTTP TIMEOUT (the token was not requested — the filter above ruled that out), not a cancel.
            LogRemoteHarvestFailed(logger, ex);
            return null;
        }
    }

    // F4-4b — ACL-översättning: JobTech must_have/nice_to_have-SKILLS → neutrala
    // Application-Requirements (CTO Decision 1A: bara skills v1 — concept_id delar
    // skill-namespace med title/description-extraktionen + framtida CV, så de är
    // direkt jämförbara. languages/education/work_experiences bevaras i raw_payload
    // av POCO:n men blir inga Requirement-termer ännu). De redan-länkade koncepten
    // kräver ingen NLP/taxonomi-match (till skillnad mot description-skill-passet).
    private static List<JobAdRequirement> MapRequirements(JobTechHit hit)
    {
        var requirements = new List<JobAdRequirement>();
        AddSkillRequirements(hit.MustHave?.Skills, ExtractedTermSource.MustHave, requirements);
        AddSkillRequirements(hit.NiceToHave?.Skills, ExtractedTermSource.NiceToHave, requirements);
        return requirements;
    }

    // weight: null → golvet 0.0 (ExtractedTerm.Weight kräver finit ≥ 0 — null
    // kraschar VO-invarianten). Krav med blank concept_id/label droppas (paritet
    // hit-skip-disciplinen — ett kravkoncept utan id/label är inte matchbart).
    private static void AddSkillRequirements(
        List<JobTechRequirementConcept>? skills,
        ExtractedTermSource source,
        List<JobAdRequirement> into)
    {
        if (skills is null)
            return;
        foreach (var skill in skills)
        {
            var conceptId = skill.ConceptId?.Trim();
            var label = skill.Label?.Trim();
            if (string.IsNullOrWhiteSpace(conceptId) || string.IsNullOrWhiteSpace(label))
                continue;
            into.Add(new JobAdRequirement(source, conceptId, label, skill.Weight ?? 0.0));
        }
    }

    private static string? FirstNonMailtoUrl(
        string? webpageUrl,
        IReadOnlyList<JobTechSourceLink>? sourceLinks,
        string? applicationDetailsUrl)
    {
        // v2-prioritet: webpage_url först.
        if (IsValidNonMailto(webpageUrl))
            return webpageUrl;

        // v1-fallback: source_links[0].url.
        if (sourceLinks is not null)
        {
            foreach (var link in sourceLinks)
            {
                if (IsValidNonMailto(link.Url))
                    return link.Url;
            }
        }

        // Sista fallback: application_details.url (kan vara mailto i prod-data).
        if (IsValidNonMailto(applicationDetailsUrl))
            return applicationDetailsUrl;

        return null;
    }

    private static bool IsValidNonMailto(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Platsbanken snapshot fetch startad.")]
    private static partial void LogSnapshotStarted(ILogger logger);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Platsbanken snapshot fetch klar — {ConvertedCount}/{TotalCount} items konverterade.")]
    private static partial void LogSnapshotCompleted(ILogger logger, int convertedCount, int totalCount);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Debug,
        Message = "Platsbanken hit {ExternalId} hoppas över — saknar obligatoriska fält.")]
    private static partial void LogHitSkipped(ILogger logger, string externalId);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Warning,
        Message = "Platsbanken snapshot trunkerad mid-stream (attempt={Attempt}, parsade={Total} före brott) — gör om hämtning. Redan-yieldade items är idempotenta via UNIQUE-index.")]
    private static partial void LogSnapshotTruncatedRetrying(
        ILogger logger, Exception exception, int attempt, int total);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Warning,
        Message = "Platsbanken snapshot trunkerad mid-stream — bounded retry uttömd efter {Attempt} försök ({ConvertedCount}/{TotalCount} konverterade). Avslutar gracefully; hybrid stream-katch-up + nästa cron fyller resten (ADR 0032-amendment 2026-05-16). Ingen Hangfire-retry-storm.")]
    private static partial void LogSnapshotTruncatedGivingUp(
        ILogger logger, Exception exception, int attempt, int convertedCount, int totalCount);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Debug,
        Message = "Platsbanken refetch ExternalId={ExternalId} — 404 från källan, hoppas över (ej arkivering — retention-flödet skiljt).")]
    private static partial void LogRefetchNotFound(ILogger logger, string externalId);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Information,
        Message = "Platsbanken remote-harvest klar — {RemoteCount} annonser klassade som distans av AF.")]
    private static partial void LogRemoteHarvestCompleted(ILogger logger, int remoteCount);

    [LoggerMessage(EventId = 5008, Level = LogLevel.Warning,
        Message = "Platsbanken remote-harvest misslyckades — remote-kolumnen lämnas orörd denna körning (fail-safe; ingen annons flippas till false). Nästa lyckade snapshot rekoncilierar.")]
    private static partial void LogRemoteHarvestFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Warning,
        Message = "Platsbanken remote-harvest gav NOLL träffar (lyckad respons, tom mängd) — behandlas som anomali (AF:s remote-korpus är normalt ~660). Remote-kolumnen lämnas orörd (fail-safe; ingen annons flippas till false).")]
    private static partial void LogRemoteHarvestEmpty(ILogger logger);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Warning,
        Message = "Platsbanken-ström trunkerad mid-stream — avslutar gracefully (ingen retry, till skillnad mot snapshot). Overlap-fönstret (15 min) + nästa snapshot fyller den tappade svansen; upserts idempotenta via UNIQUE-index. Ihållande trunkering = JobTech-instabilitet, undersök. ADR 0032 §5 + #483.")]
    private static partial void LogStreamTruncated(ILogger logger, Exception exception);
}
