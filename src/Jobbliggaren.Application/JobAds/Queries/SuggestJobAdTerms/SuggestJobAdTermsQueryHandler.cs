using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C + ADR 0067 Beslut 5a — utökad typeahead-union. Slår ihop
/// taxonomi-snapshot-prefix (<see cref="ITaxonomyReadModel.SuggestByPrefixAsync"/>,
/// in-memory ACL), arbetsgivare (#1546,
/// <see cref="IEmployerDisambiguationQuery.SuggestActiveEmployersAsync"/>) och lokal
/// <c>job_ads.Title</c> ILIKE-prefix (befintlig gren).
/// Tunn adapter — union + dedup + Take(limit) i Application; ingen Npgsql-specifik
/// LINQ (titel-grenen är provider-agnostisk <c>EF.Functions.Like</c>).
/// <para>
/// <b>Tre block, i ordningen dimension → dimension → fritext.</b> Arbetsgivar-blocket bär
/// dessutom en egen budget och en egen minimilängd, av skäl som står vid respektive konstant.
/// </para>
/// <para>
/// <b>Två DB-rundturer per tangenttryckning</b> (arbetsgivare + titlar; taxonomin är en
/// process-cachad snapshot). De körs SEKVENTIELLT och får inte parallelliseras med
/// <c>Task.WhenAll</c>: båda läser samma scoped <c>IAppDbContext</c>, och samtidig användning av
/// en DbContext kastar. Skrivet här därför att en parallellisering annars ser ut som en gratis
/// vinst.
/// </para>
/// </summary>
public sealed class SuggestJobAdTermsQueryHandler(
    IAppDbContext db, ITaxonomyReadModel taxonomy, IEmployerDisambiguationQuery employers)
    : IQueryHandler<SuggestJobAdTermsQuery, IReadOnlyList<SuggestionDto>>
{
    /// <summary>
    /// #1546 — how many employer rows the union will SHOW. Not a DoS floor and not the port's cap:
    /// the port is asked for the whole <c>Limit</c> (see below), and this bounds only how much of the
    /// result the employer block may occupy.
    /// <para>
    /// It exists because the employer block is not shaped like its two siblings. Taxonomy and title
    /// match a PREFIX against bounded or curated sets; employer matches a <c>%contains%</c> against the
    /// whole ad corpus and ranks by ad count. So a fragment like "ab", "and" or "kommun" — a substring
    /// of a great many Swedish company names — would otherwise return the corpus's largest employers,
    /// in size order, and push out every title and taxonomy suggestion the surface already promised.
    /// A budget on the new block only (senior-cto-advisor 2026-08-31) leaves the delivered
    /// taxonomy-over-title priority exactly as it was.
    /// </para>
    /// </summary>
    private const int EmployerSuggestionBudget = 3;

    /// <summary>
    /// #1546 — the shortest term the employer branch will run at all.
    /// <para>
    /// <b>The reason is physical, not stylistic:</b> a GIN trigram index is built from 3-grams, so it
    /// cannot serve a <c>LIKE '%xx%'</c> shorter than three characters. A shorter term does not merely
    /// perform worse — it forces a full-corpus evaluation, once per keystroke.
    /// </para>
    /// <para>
    /// <b>The same number lives in two other places, deliberately duplicated.</b>
    /// <c>JobAdSearchComposition.includeSubstringLike</c> gates the q-disjunction's two substring arms,
    /// and <c>Program.RunExplainSearchAsync</c>'s <c>substringArms</c> mirrors it for the EXPLAIN tool.
    /// They are named here by SYMBOL, never by line number, which rots. They are not shared through a
    /// constant because the shared knowledge is a fact about <c>pg_trgm</c> that this repo does not own
    /// and cannot change; the three gates have different failure semantics and are allowed to diverge.
    /// Composition's gate drops an OR-arm from a query that still answers through FTS; this one
    /// switches a whole block off, which is a product behaviour, not a Postgres fact.
    /// </para>
    /// <para>
    /// ⚠ <b>This is NOT the validator's floor and must not be reconciled with it.</b>
    /// <c>SuggestJobAdTermsQueryValidator</c> enforces <c>MinimumLength(2)</c> and carries a standing
    /// #831 instruction not to touch it. The two numbers are two different rules, so the surface
    /// legitimately holds both: at exactly two characters the user gets taxonomy and title suggestions
    /// and no employers.
    /// </para>
    /// </summary>
    private const int MinTrigramServableTermLength = 3;

    public async ValueTask<IReadOnlyList<SuggestionDto>> Handle(
        SuggestJobAdTermsQuery query, CancellationToken cancellationToken)
    {
        // (i) Taxonomi-prefix — in-memory ACL-snapshot (Län/Kommun/Yrkesområde/
        // Yrkesgrupp; occupation-name utesluts, VAL 4). Bryter EJ ADR 0043:s
        // extern-hop-förbud. Hela limit:en begärs; union cappar sedan totalen.
        var taxonomyHits = await taxonomy.SuggestByPrefixAsync(
            query.Prefix, query.Limit, cancellationToken);

        // (ii) #1546 — arbetsgivar-prefix. Grinden kollas FÖRE await:et, så ett
        // för kort prefix hoppar över hela portanropet i stället för att kasta
        // dess resultat. Porten får HELA query.Limit, aldrig budgeten: portens
        // egen docblock varnar att en cap före ett filter den inte ser krymper
        // listan tyst, och F3:s uteslutning sker här nere. Vid default-limit 10
        // och budget 3 finns alltså 7 rader marginal för uteslutna rader —
        // antagandet skrivet, inte underförstått (security-auditor Minor 2).
        var employerHits = query.Prefix.Length >= MinTrigramServableTermLength
            ? await employers.SuggestActiveEmployersAsync(
                query.Prefix, query.Limit, cancellationToken)
            : [];

        // (iii) Titel-prefix (ADR 0042 Beslut C, oförändrad gren). LIKE-metatecken
        // escapas så left-anchor bevaras (btree functional partial-index
        // användbart; ej seq-scan-DoS). Explicit ESCAPE '\'. .ToLower() →
        // SQL LOWER(col) (CA1304/CA1311-suppress: LINQ-translation, ej runtime).
        const string escapeChar = "\\";
        var pattern = LikePattern.EscapePrefix(query.Prefix).ToLowerInvariant() + "%";

#pragma warning disable CA1304, CA1311
        var titles = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .Where(j => EF.Functions.Like(j.Title.ToLower(), pattern, escapeChar))
            .Select(j => j.Title)
            .Distinct()
            .OrderBy(t => t)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311

        // Union: taxonomi först (deterministisk enum→label-ordning från porten),
        // sedan titlar. Dedup-nyckel = (Kind, ConceptId) för taxonomi-noder
        // (ConceptId alltid satt) och (Title, Label) för titlar (Title saknar
        // ConceptId) — en taxonomi-nod och en titel kan dela label utan att vara
        // samma förslag (olika Kind). Cap till limit (validator garanterar 1–20).
        var result = new List<SuggestionDto>(query.Limit);
        var seen = new HashSet<(SuggestionKind, string)>();

        foreach (var hit in taxonomyHits)
        {
            if (result.Count >= query.Limit)
                break;
            if (seen.Add((hit.Kind, hit.ConceptId)))
                result.Add(new SuggestionDto(hit.Kind, hit.ConceptId, hit.Label));
        }

        // #1546 — arbetsgivare mellan taxonomi och titel. Ordningen är inte smak:
        // den levererade regeln är DIMENSION före FRITEXT (taxonomi sätter en
        // filterdimension, Title är residual q), och en arbetsgivare sätter
        // ?employer=<org.nr> — ADR 0087:s kanoniska nyckel. Samma regel, ny
        // medlem. Positionen ÄR dessutom den renderade ordningen: typeaheaden
        // målar en platt lista i API-ordning och sorterar aldrig om.
        var employerCount = 0;
        foreach (var employer in employerHits)
        {
            if (result.Count >= query.Limit || employerCount >= EmployerSuggestionBudget)
                break;

            // F3 (CTO-bind) / ADR 0087 D8(c) — GRIND 1 av 2: en enskild firmas
            // org.nr KAN vara innehavarens personnummer, och en arbetsgivar-
            // typeahead som räknar upp dem vore en namnkatalog över fysiska
            // personer. Uteslutningen bor HÄR, i handlern, aldrig i
            // Infrastructure och aldrig via indexets predikat (det är indexets
            // form, inte VO:ns definition — en andra heuristik som måste hållas
            // i lås). Samma enkelkällade detektor som grind 2 i
            // SuggestionDto.ForEmployer, men beräknad OBEROENDE: stryker någon
            // den ena står den andra kvar.
            // Nåbarheten består: ?q=<namn> når firmans annonser utan org.nr.
            if (OrganizationNumber.FromTrusted(employer.OrganizationNumber).IsPersonnummerShaped())
                continue;

            // Dedup på ORG.NR, inte på etiketten: två distinkta juridiska
            // personer kan dela namn — det är precis Volvo×20-fällan som gjorde
            // org.nr till kanonisk nyckel. Etikett-nyckling skulle tyst kollapsa
            // just de rader funktionen finns för att särskilja.
            if (!seen.Add((SuggestionKind.Employer, employer.OrganizationNumber)))
                continue;

            result.Add(SuggestionDto.ForEmployer(
                employer.OrganizationNumber, employer.CompanyName, employer.AdCount));
            employerCount++;
        }

        foreach (var title in titles)
        {
            if (result.Count >= query.Limit)
                break;
            if (seen.Add((SuggestionKind.Title, title)))
                result.Add(new SuggestionDto(SuggestionKind.Title, null, title));
        }

        return result;
    }
}
