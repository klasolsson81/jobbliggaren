using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

// ADR 0062 — FTS-hybrid-suite för jobbannons-sök. Verifierar q-grenen i
// JobAdSearchQuery.ApplyCriteria mot riktig Testcontainers-Postgres
// (F6P4FtsSearchVector-migrationen appliceras av ApiFactory):
//
//   search_vector @@ websearch_to_tsquery('swedish', q)   -- FTS-primärväg
//   OR lower(title) LIKE '%q%'                            -- title-substring-fallback
//
// Kritiska beteenden som täcks:
//  * svensk stemming (lärare/läraren/lärares → samma lexem) via FTS
//  * search_vector spänner BÅDE title OCH description → helt description-ord matchbart
//  * title-LIKE ger mitt-i-ord-substring ENBART mot titeln
//  * description-LIKE är BORTTAGET (perf-rotorsak) → mitt-i-ord-delsträng av
//    ett description-ord matchar INTE längre (negativ regression)
//  * JobAdSortBy.Relevance rangordnar via ts_rank — title-LIKE-träffar
//    (ts_rank 0) hamnar efter FTS-träffar
//  * IsNew/Since-projektionen
//  * sortering (PublishedAt/ExpiresAt) — coverage flyttad från det omskrivna
//    ListJobAdsQueryHandlerTests-unit-testet (CLAUDE.md §7 — coverage får ej sänkas)
//
// Sök-kompositionen är internal (JobAdSearchQuery, InternalsVisibleTo
// Api.IntegrationTests). ListJobAdsQueryHandler är en tunn adapter; den
// instansieras här med en riktig JobAdSearchQuery.
[Collection("Api")]
public class ListJobAdsFtsTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Seed:ar en manuell JobAd med kontrollerad title/description/publishedAt.
    // Manuell (ej Import) → inget raw_payload, inga ssyk/region generated
    // columns — irrelevant för FTS-grenen, som bara läser search_vector + title.
    private async Task<Guid> SeedJobAdAsync(
        string title,
        string description,
        CancellationToken ct,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var jobAd = JobAd.Create(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: description,
            url: $"https://example.com/jobs/{Guid.NewGuid():N}",
            source: JobSource.Manual,
            publishedAt: publishedAt ?? clock.UtcNow.AddDays(-1),
            expiresAt: expiresAt,
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    private static ListJobAdsQueryHandler CreateHandler(IServiceScope scope) =>
        new(new JobAdSearchQuery(
            scope.ServiceProvider.GetRequiredService<AppDbContext>()));

    // 1. FTS svensk stemming — websearch_to_tsquery('swedish', …) reducerar
    //    böjningsformer (lärare/läraren/lärares) till samma lexem.
    [Fact]
    public async Task ApplyCriteria_FtsSwedishStemming_MatchesInflectedForms()
    {
        var ct = TestContext.Current.CancellationToken;
        var marker = $"larartoken{Guid.NewGuid():N}"[..18];

        // Träff 1: token i titel i grundform.
        await SeedJobAdAsync($"Lärare {marker} till grundskolan", "Beskrivning", ct);
        // Träff 2: token + böjd form av "lärare" i description.
        await SeedJobAdAsync($"Annons {marker}", "Vi söker en läraren omgående.", ct);
        // Ej träff: saknar marker-token helt.
        await SeedJobAdAsync("Helt orelaterad annons", "Ingenting matchande här.", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        // "lärare <marker>" → websearch_to_tsquery ger AND-lexem; båda
        // seedade träffarna innehåller marker + en form av lexemet lärare
        // (grundform respektive böjd "läraren") → stemming gör båda matchbara.
        var result = await handler.Handle(
            new ListJobAdsQuery(Q: $"lärare {marker}"), ct);

        result.TotalCount.ShouldBe(2);
        result.Items.ShouldContain(i => i.Title == $"Lärare {marker} till grundskolan");
        result.Items.ShouldContain(i => i.Title == $"Annons {marker}");
    }

    // 2. title-LIKE mitt-i-ord-fallback — en delsträng mitt i ett titelord
    //    (ej lexem, ej prefix) matchar via lower(title) LIKE '%q%'.
    [Fact]
    public async Task ApplyCriteria_TitleLikeFallback_MatchesMidWordSubstringInTitle()
    {
        var ct = TestContext.Current.CancellationToken;
        var marker = $"sysdev{Guid.NewGuid():N}"[..14];

        // "temutveck" är en mitt-i-ord-delsträng av "Systemutvecklare".
        await SeedJobAdAsync($"Systemutvecklare {marker} sökes", "Beskrivning", ct);
        await SeedJobAdAsync($"Annan roll {marker}", "Beskrivning", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Q: $"temutveck {marker}"), ct);

        // websearch_to_tsquery → "temutveck" & "<marker>"; "temutveck" är inget
        // lexem så FTS-grenen träffar inte. title-LIKE-grenen matchar dock hela
        // q-strängen mot titeln: "systemutvecklare … sökes" innehåller den ej —
        // q-strängen är "temutveck <marker>" som helhet. Verifiera mot ett
        // ensamt mitt-i-ord-q istället för säker assertion på title-LIKE.
        result.ShouldNotBeNull();

        // Renodlad mitt-i-ord-q (utan extra token) → title-LIKE matchar
        // "Systemutvecklare …".
        var midWord = await handler.Handle(new ListJobAdsQuery(Q: "temutveck"), ct);
        midWord.Items.ShouldContain(i => i.Title == $"Systemutvecklare {marker} sökes");
    }

    // 3. description-ord matchar via FTS — search_vector spänner description,
    //    så ett helt unikt ord i description är FTS-matchbart.
    [Fact]
    public async Task ApplyCriteria_Fts_MatchesWholeWordInDescription()
    {
        var ct = TestContext.Current.CancellationToken;
        var descWord = $"unikord{Guid.NewGuid():N}"[..16];

        // Titeln saknar token; description har det som ett helt ord.
        await SeedJobAdAsync(
            "Annons utan token i titel", $"Vi erbjuder {descWord} som förmån.", ct);
        await SeedJobAdAsync("Orelaterad annons", "Ingen matchning alls.", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(new ListJobAdsQuery(Q: descWord), ct);

        result.TotalCount.ShouldBe(1);
        result.Items.ShouldHaveSingleItem().Title.ShouldBe("Annons utan token i titel");
    }

    // 4. NEGATIV regression — bevisar att description-LIKE är borttaget.
    //    En mitt-i-ord-delsträng av ett description-ORD (ej lexem, ej i titeln)
    //    ger 0 träffar. Under den gamla description-ILIKE-grenen hade detta
    //    matchat; under ADR 0062 gör det inte det.
    [Fact]
    public async Task ApplyCriteria_DescriptionMidWordSubstring_DoesNotMatch_RegressionGate()
    {
        var ct = TestContext.Current.CancellationToken;
        var anchor = $"negtest{Guid.NewGuid():N}"[..14];

        // Titeln innehåller anchor (så annonsen är hittbar via ett kontroll-q),
        // men INTE delsträngen "temutveck". description har "Systemutvecklare".
        await SeedJobAdAsync(
            $"Annons {anchor} utan token",
            "Systemutvecklare behövs omgående för uppdraget.", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        // "temutveck" är en mitt-i-ord-delsträng av description-ordet
        // "Systemutvecklare" — ej lexem, ej i titeln. description-LIKE är
        // borttaget → 0 träffar.
        var result = await handler.Handle(new ListJobAdsQuery(Q: "temutveck"), ct);

        result.Items.ShouldNotContain(i => i.Title == $"Annons {anchor} utan token");

        // Kontroll: annonsen ÄR seedad och hittbar via anchor i titeln
        // (title-LIKE) — bevisar att 0-träffen beror på borttagen
        // description-LIKE, inte på misslyckad seed.
        var control = await handler.Handle(new ListJobAdsQuery(Q: anchor), ct);
        control.Items.ShouldContain(i => i.Title == $"Annons {anchor} utan token");
    }

    // 5. ts_rank-relevans — FTS-träff (ts_rank > 0) rankas före title-LIKE-only-
    //    träff (ts_rank 0). PublishedAt DESC är tiebreaker mellan likvärdiga
    //    FTS-träffar.
    [Fact]
    public async Task ApplyCriteria_RelevanceSort_FtsMatchOutranksTitleLikeOnlyMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = _factory.Services.GetRequiredService<IDateTimeProvider>();
        var word = $"konsulttoken{Guid.NewGuid():N}"[..18];

        // FTS-träff: word är ett helt ord (lexem) i titeln → ts_rank > 0.
        var ftsId = await SeedJobAdAsync(
            $"{word} sökes nu", "Beskrivning för FTS-träff", ct,
            publishedAt: clock.UtcNow.AddDays(-5));
        // title-LIKE-only-träff: word förekommer mitt i ett titelord, inte som
        // eget lexem → FTS missar, title-LIKE matchar → ts_rank 0.
        var likeId = await SeedJobAdAsync(
            $"Super{word}roll ledig", "Beskrivning för LIKE-träff", ct,
            publishedAt: clock.UtcNow.AddDays(-3));

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(SortBy: JobAdSortBy.Relevance, Q: word), ct);

        result.TotalCount.ShouldBe(2);
        // FTS-träff (ts_rank > 0) först, title-LIKE-only-träff (ts_rank 0) sist.
        result.Items[0].Id.ShouldBe(ftsId);
        result.Items[1].Id.ShouldBe(likeId);
    }

    [Fact]
    public async Task ApplyCriteria_RelevanceSort_TiebreaksEqualFtsMatchesByPublishedAtDesc()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = _factory.Services.GetRequiredService<IDateTimeProvider>();
        var word = $"likvardig{Guid.NewGuid():N}"[..18];

        // Två annonser där word är lexem i titeln på samma sätt → likvärdig
        // ts_rank. PublishedAt DESC avgör ordningen.
        var older = await SeedJobAdAsync(
            $"{word} äldre annons", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddDays(-10));
        var newer = await SeedJobAdAsync(
            $"{word} nyare annons", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddDays(-1));

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(SortBy: JobAdSortBy.Relevance, Q: word), ct);

        result.TotalCount.ShouldBe(2);
        result.Items[0].Id.ShouldBe(newer);
        result.Items[1].Id.ShouldBe(older);
    }

    // 6. IsNew/Since — Since i det förflutna ⇒ nyligen publicerade annonser får
    //    IsNew=true; ingen Since ⇒ IsNew=false. (Bevarar assertionen från det
    //    borttagna RelevanceSort-testet i ListJobAdsMultiFilterTests.)
    [Fact]
    public async Task ApplyCriteria_IsNew_ReflectsSinceWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var word = $"isnewtoken{Guid.NewGuid():N}"[..18];

        await SeedJobAdAsync($"{word} annons A", "Beskrivning", ct);
        await SeedJobAdAsync($"{word} annons B", "Beskrivning", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        // Since i det förflutna → seedade annonser (publishedAt = now-1d) ligger
        // inom fönstret → IsNew=true.
        var withSince = await handler.Handle(
            new ListJobAdsQuery(Q: word, Since: DateTimeOffset.UtcNow.AddDays(-30)), ct);
        withSince.TotalCount.ShouldBe(2);
        withSince.Items.ShouldAllBe(i => i.IsNew);

        // Ingen Since → IsNew=false.
        var noSince = await handler.Handle(new ListJobAdsQuery(Q: word), ct);
        noSince.TotalCount.ShouldBe(2);
        noSince.Items.ShouldAllBe(i => !i.IsNew);
    }

    // 7. Sortering — coverage-bevarande, flyttad från det omskrivna
    //    ListJobAdsQueryHandlerTests-unit-testet (CLAUDE.md §7). Filtrerar på ett
    //    unikt token i titeln så bara de seedade annonserna räknas (delad
    //    [Collection("Api")]-DB).
    [Fact]
    public async Task ApplyCriteria_PublishedAtDesc_ReturnsNewestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = _factory.Services.GetRequiredService<IDateTimeProvider>();
        var token = $"sortdesc{Guid.NewGuid():N}"[..16];

        await SeedJobAdAsync($"{token} Newest", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddHours(-1));
        await SeedJobAdAsync($"{token} Oldest", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddHours(-3));
        await SeedJobAdAsync($"{token} Middle", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddHours(-2));

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Q: token, SortBy: JobAdSortBy.PublishedAtDesc), ct);

        result.Items.Select(i => i.Title)
            .ShouldBe([$"{token} Newest", $"{token} Middle", $"{token} Oldest"]);
    }

    [Fact]
    public async Task ApplyCriteria_PublishedAtAsc_ReturnsOldestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = _factory.Services.GetRequiredService<IDateTimeProvider>();
        var token = $"sortasc{Guid.NewGuid():N}"[..16];

        await SeedJobAdAsync($"{token} Newest", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddHours(-1));
        await SeedJobAdAsync($"{token} Oldest", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddHours(-3));
        await SeedJobAdAsync($"{token} Middle", "Beskrivning", ct,
            publishedAt: clock.UtcNow.AddHours(-2));

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Q: token, SortBy: JobAdSortBy.PublishedAtAsc), ct);

        result.Items.Select(i => i.Title)
            .ShouldBe([$"{token} Oldest", $"{token} Middle", $"{token} Newest"]);
    }

    [Fact]
    public async Task ApplyCriteria_ExpiresAtAsc_NullExpiresAtSortedLast()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = _factory.Services.GetRequiredService<IDateTimeProvider>();
        var token = $"expasc{Guid.NewGuid():N}"[..16];
        var basePublished = clock.UtcNow.AddDays(-1);

        await SeedJobAdAsync($"{token} ExpiresLater", "Beskrivning", ct,
            publishedAt: basePublished, expiresAt: clock.UtcNow.AddDays(7));
        await SeedJobAdAsync($"{token} NoExpiry", "Beskrivning", ct,
            publishedAt: basePublished, expiresAt: null);
        await SeedJobAdAsync($"{token} ExpiresSoon", "Beskrivning", ct,
            publishedAt: basePublished, expiresAt: clock.UtcNow.AddDays(1));

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Q: token, SortBy: JobAdSortBy.ExpiresAtAsc), ct);

        result.Items.Select(i => i.Title).ShouldBe(
            [$"{token} ExpiresSoon", $"{token} ExpiresLater", $"{token} NoExpiry"]);
    }

    [Fact]
    public async Task ApplyCriteria_ExpiresAtDesc_NullExpiresAtSortedLast()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = _factory.Services.GetRequiredService<IDateTimeProvider>();
        var token = $"expdesc{Guid.NewGuid():N}"[..16];
        var basePublished = clock.UtcNow.AddDays(-1);

        await SeedJobAdAsync($"{token} ExpiresLater", "Beskrivning", ct,
            publishedAt: basePublished, expiresAt: clock.UtcNow.AddDays(7));
        await SeedJobAdAsync($"{token} NoExpiry", "Beskrivning", ct,
            publishedAt: basePublished, expiresAt: null);
        await SeedJobAdAsync($"{token} ExpiresSoon", "Beskrivning", ct,
            publishedAt: basePublished, expiresAt: clock.UtcNow.AddDays(1));

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(
            new ListJobAdsQuery(Q: token, SortBy: JobAdSortBy.ExpiresAtDesc), ct);

        result.Items.Select(i => i.Title).ShouldBe(
            [$"{token} ExpiresLater", $"{token} ExpiresSoon", $"{token} NoExpiry"]);
    }
}
