using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Fas C2 — Platsbanken sök-paritet (ADR 0067 Beslut 1 + 6 + 7 C2-raden;
    /// CTO-dom (c)/(d)/(f) 2026-06-09; architect F3 2026-06-09).
    ///
    /// <para><b>Tre delar i Up(), i bindande ordning:</b>
    /// (1) <c>DELETE FROM recent_job_searches</c> FÖRE DDL — raderna är
    /// självåterbyggande cache-data utan audit-trail-värdighet (CTO (d);
    /// dev-DB hade 3 rader varav 1 med rå SSYK-kod, omappbar). NOT NULL-
    /// AddColumn utan default failar annars på befintliga rader.
    /// (2) recent-DDL: DROP <c>ssyk_list</c> + ADD <c>occupation_group_list</c>
    /// + <c>municipality_list</c> (text[], NOT NULL). DROP+ADD, INTE RENAME —
    /// kolumnen byter semantik (occupation-name → ssyk-level-4), och EN kolumn
    /// kan inte rename:as till TVÅ; RENAME skulle ljuga i migrations-historiken
    /// (architect F3).
    /// (3) Reverse-lookup-transform av <c>saved_searches.criteria</c> (jsonb):
    /// legacy-nyckeln <c>"Ssyk"</c> (occupation-name-ids) ersätts av
    /// <c>"OccupationGroup"</c> (ssyk-level-4-ids, sorterad + distinct i
    /// LAGRAD form per ADR 0042 Beslut B invariant 1; <c>COLLATE "C"</c> =
    /// byte-ordning = StringComparer.Ordinal-paritet).</para>
    ///
    /// <para><b>Mappningskälla:</b> den FRUSNA migration-ägda embedded-resursen
    /// <c>Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json</c>
    /// (2179 poster, JobTech broader-relation, 2179/2179 exakt 1 parent
    /// live-verifierat 2026-06-09). Resursen regenereras ALDRIG —
    /// migrations-immutabilitet (Fowler/Sadalage): en framtida v31-snapshot
    /// får inte tyst ändra vad denna migration gör vid replay på färsk DB.
    /// Migrationen läser ENDAST embedded-resursen — ALDRIG
    /// <c>taxonomy_concepts</c> (seedern kör EFTER migrationer; tabellen kan
    /// inte litas på — seeder-ordering-fällan strukturellt eliminerad) och
    /// ALDRIG den levande <c>taxonomy-snapshot.json</c>.</para>
    ///
    /// <para><b>Fail-loud:</b> bär någon rad ett <c>Ssyk</c>-id utanför
    /// mappningen ABORTar migrationen med tydligt fel (Saltzer/Schroeder
    /// fail-safe default) — tyst droppning kunde amputera en bevaknings
    /// yrke-dimension eller lämna raden i tom-invariant-brott. Med 0 rader i
    /// <c>saved_searches</c> vid apply (dev-DB-inventering 2026-06-09) fyrar
    /// den aldrig; på färska DBs är den trivialt no-op.</para>
    ///
    /// <para><b>Idempotens:</b> transformen tar bort <c>"Ssyk"</c>-nyckeln och
    /// predikatet är nyckel-EXISTENS (<c>criteria ? 'Ssyk'</c>, inkl.
    /// <c>"Ssyk":[]</c> — gamla Write emitterade alltid nyckeln) → omkörning
    /// träffar 0 rader. occupation-name- och ssyk-level-4-id-universerna är
    /// disjunkta → dubbelmappning omöjlig by construction. SQL:en genereras
    /// deterministiskt ur den frusna resursen → <c>dotnet ef migrations
    /// script --idempotent</c> producerar samma SQL vid varje generering.</para>
    ///
    /// <para><b>Deploy-ordning:</b> migrationen appliceras FÖRE ny binär
    /// startas — nya <c>SearchCriteriaJsonConverter</c> fail-loud:ar på
    /// legacy-<c>"Ssyk"</c>-nyckeln (500 vid läsning mot omigrerad DB).
    /// Skrivvägen för <c>Ssyk</c> stängdes i samma C2-batch → inga nya
    /// legacy-rader kan uppstå efter apply.</para>
    ///
    /// <para><b>Down() är dokumenterat LOSSY:</b> DDL:en är mekaniskt
    /// reversibel (återskapar <c>ssyk_list</c>), men (i) raderade recent-rader
    /// är borta (cache-data, självåterbyggande), (ii) jsonb-transformen är
    /// irreversibel — grupp→yrken är 1-till-många, original-occupation-names
    /// kan inte återskapas. Accepterat per CTO (f): 0 rader vid apply, ingen
    /// prod existerar (ADR 0066), och kunskapen occupation-name→grupp är
    /// committad i resursen för all framtid.</para>
    /// </summary>
    public partial class C2SearchParityReverseLookupAndRecentExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) Radera recent-rader FÖRE DDL (CTO (d)): NOT NULL-AddColumn
            // utan default failar på befintliga rader; raderna är efemär,
            // självåterbyggande cache-data (cap-20-eviction).
            migrationBuilder.Sql("DELETE FROM recent_job_searches;");

            // (2) DROP+ADD — INTE RENAME (architect F3: kolumnen byter
            // semantik, en kolumn kan inte bli två; scaffoldens RENAME-
            // gissning förkastad).
            migrationBuilder.DropColumn(
                name: "ssyk_list",
                table: "recent_job_searches");

            migrationBuilder.AddColumn<List<string>>(
                name: "occupation_group_list",
                table: "recent_job_searches",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<List<string>>(
                name: "municipality_list",
                table: "recent_job_searches",
                type: "text[]",
                nullable: false);

            // (3) Reverse-lookup-transform (set-baserad, frusen resurs).
            foreach (var sql in BuildReverseLookupSql())
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // LOSSY (se klass-XML-doc): recent-rader raderas igen (post-C2-
            // captures bär occupation_group/municipality som ssyk_list inte
            // kan representera; NOT NULL-AddColumn kräver dessutom tom tabell)
            // och saved_searches.criteria återtransformeras INTE
            // (grupp→yrken är 1-till-många — irreversibel).
            migrationBuilder.Sql("DELETE FROM recent_job_searches;");

            migrationBuilder.DropColumn(
                name: "municipality_list",
                table: "recent_job_searches");

            migrationBuilder.DropColumn(
                name: "occupation_group_list",
                table: "recent_job_searches");

            migrationBuilder.AddColumn<List<string>>(
                name: "ssyk_list",
                table: "recent_job_searches",
                type: "text[]",
                nullable: false);
        }

        // ── Reverse-lookup-SQL (architect F3-skissen, bindande) ──────────────
        //
        // internal (InternalsVisibleTo JobbPilot.Api.IntegrationTests) så
        // Testcontainers-testerna kör EXAKT den SQL migrationen kör — ingen
        // testkopia som kan glida (DRY på knowledge-piece-nivå).

        internal const string FrozenMappingResourceName =
            "JobbPilot.Infrastructure.Persistence.Migrations.Resources.occupation-name-to-ssyk-level-4.v30.json";

        // ~500 rader per VALUES-sats (architect F3).
        private const int InsertBatchSize = 500;

        // Defense-in-depth: resursen är committad (ingen injection-yta i sak),
        // men varje id valideras ändå mot concept-id-grammatiken innan det
        // interpoleras i SQL. Fail-loud vid avvikelse — ingen escaping-
        // gymnastik, ingen tyst sanering. Samma grammatik som Domain-regexen
        // för concept-ids (1-32 tecken, alfanumeriskt + _-). \z (inte $) —
        // .NET-$ matchar före en avslutande \n; här sker ingen trim före
        // SQL-interpolation så ankaret måste vara exakt (security-auditor
        // C2 Minor 1 2026-06-10).
        private static readonly Regex ConceptIdPattern =
            new(@"^[A-Za-z0-9_-]{1,32}\z", RegexOptions.Compiled);

        /// <summary>
        /// Genererar den fullständiga, deterministiska statement-sekvensen för
        /// reverse-lookup-transformen: temp-tabell → INSERT-batchar (ur den
        /// frusna resursen) → fail-loud-DO-block → jsonb-UPDATE → DROP temp.
        /// Ren SQL utan parametrar → fungerar identiskt för
        /// <c>database update</c>, <c>migrations script --idempotent</c> och
        /// psql-apply.
        /// </summary>
        internal static IReadOnlyList<string> BuildReverseLookupSql()
        {
            var mappings = LoadFrozenMapping();

            var statements = new List<string>
            {
                """
                CREATE TEMP TABLE _occname_to_ssyk4 (
                    occupation_name_id text PRIMARY KEY,
                    ssyk4_id text NOT NULL);
                """,
            };

            // Sorterat på occupation-name-id (ordinal) → deterministisk
            // statement-ordning oavsett JSON-parserns ordningsgarantier.
            var ordered = mappings
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < ordered.Count; i += InsertBatchSize)
            {
                var batch = ordered.Skip(i).Take(InsertBatchSize);
                var sb = new StringBuilder(
                    "INSERT INTO _occname_to_ssyk4 (occupation_name_id, ssyk4_id) VALUES\n");
                sb.AppendJoin(",\n", batch.Select(kv => $"('{kv.Key}', '{kv.Value}')"));
                sb.Append(';');
                statements.Add(sb.ToString());
            }

            // Fail-loud: omappbart Ssyk-id → ABORT (Saltzer/Schroeder).
            // Predikat = nyckel-existens (criteria ? 'Ssyk') — gamla Write
            // emitterade ALLTID nyckeln, även "Ssyk":[] på Region/Q-only-rader.
            // Skalär legacy-form ("Ssyk":"id", pre-F2-historisk — gamla
            // konvertern tolererade den on-read) fångas FÖRST med pedagogiskt
            // fel i stället för rått "cannot extract elements from a scalar"
            // (security-auditor C2 Minor 2 2026-06-10); typeof-filtret i
            // array-checken hindrar att LATERAL-funktionen evalueras på
            // icke-array-rader.
            statements.Add(
                """
                DO $$
                DECLARE bad record;
                BEGIN
                    SELECT s.id INTO bad
                    FROM saved_searches s
                    WHERE s.criteria ? 'Ssyk'
                      AND jsonb_typeof(s.criteria->'Ssyk') <> 'array'
                    LIMIT 1;
                    IF FOUND THEN
                        RAISE EXCEPTION
                          'C2 reverse-lookup: saved_search % bär "Ssyk" i icke-array-form (pre-F2 skalär legacy). Migrationen abortar — normalisera raden till array-form innan apply, droppa inte tyst.',
                          bad.id;
                    END IF;

                    SELECT s.id, e.elem INTO bad
                    FROM (SELECT id, criteria FROM saved_searches
                          WHERE criteria ? 'Ssyk'
                            AND jsonb_typeof(criteria->'Ssyk') = 'array') s
                    CROSS JOIN LATERAL jsonb_array_elements_text(s.criteria->'Ssyk') AS e(elem)
                    WHERE NOT EXISTS (SELECT 1 FROM _occname_to_ssyk4 m
                                      WHERE m.occupation_name_id = e.elem)
                    LIMIT 1;
                    IF FOUND THEN
                        RAISE EXCEPTION
                          'C2 reverse-lookup: saved_search % bär omappbart Ssyk-id "%". Migrationen abortar — komplettera mappnings-resursen, droppa inte tyst.',
                          bad.id, bad.elem;
                    END IF;
                END $$;
                """);

            // Set-baserad transform. Sorterad + distinct i LAGRAD form
            // (ADR 0042 Beslut B invariant 1); COLLATE "C" = byte-ordning =
            // StringComparer.Ordinal-paritet. OBS avvikelse från F3-skissens
            // exakta syntax: COLLATE appliceras på BÅDE DISTINCT-argumentet
            // och ORDER BY-uttrycket — PostgreSQL kräver att ORDER BY-
            // uttrycket i ett DISTINCT-aggregat matchar argumentlistan
            // exakt ("in an aggregate with DISTINCT, ORDER BY expressions
            // must appear in argument list"). Semantiken är identisk
            // (COLLATE ändrar inte värdet, bara sort/jämförelse-ordningen).
            statements.Add(
                """
                UPDATE saved_searches s
                SET criteria = (s.criteria - 'Ssyk')
                    || jsonb_build_object('OccupationGroup', COALESCE(
                        (SELECT to_jsonb(array_agg(DISTINCT m.ssyk4_id COLLATE "C"
                                         ORDER BY m.ssyk4_id COLLATE "C"))
                         FROM jsonb_array_elements_text(s.criteria->'Ssyk') AS e(elem)
                         JOIN _occname_to_ssyk4 m ON m.occupation_name_id = e.elem),
                        '[]'::jsonb))
                WHERE s.criteria ? 'Ssyk';
                """);

            statements.Add("DROP TABLE _occname_to_ssyk4;");

            return statements;
        }

        // Läser den FRUSNA embedded-resursen (aldrig taxonomy_concepts,
        // aldrig levande snapshot — se klass-XML-doc). Fail-loud på saknad
        // resurs, tomt mappnings-objekt eller ogiltigt id-format.
        private static Dictionary<string, string> LoadFrozenMapping()
        {
            using var stream = typeof(C2SearchParityReverseLookupAndRecentExpansion)
                .Assembly.GetManifestResourceStream(FrozenMappingResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{FrozenMappingResourceName}' saknas — "
                    + "C2-migrationen kan inte generera reverse-lookup-SQL.");

            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("mappings", out var mappingsElement)
                || mappingsElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Mappnings-resursen saknar 'mappings'-objektet — frusen artefakt korrupt.");
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in mappingsElement.EnumerateObject())
            {
                var occupationNameId = prop.Name;
                var ssyk4Id = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : throw new InvalidOperationException(
                        $"Mappnings-värdet för '{occupationNameId}' är inte en sträng.");

                if (!ConceptIdPattern.IsMatch(occupationNameId)
                    || !ConceptIdPattern.IsMatch(ssyk4Id))
                {
                    throw new InvalidOperationException(
                        $"Ogiltigt concept-id-format i mappnings-resursen: "
                        + $"'{occupationNameId}' -> '{ssyk4Id}' (förväntat ^[A-Za-z0-9_-]{{1,32}}$). "
                        + "Fail-loud — id:t interpoleras i SQL och får inte avvika från grammatiken.");
                }

                result.Add(occupationNameId, ssyk4Id);
            }

            if (result.Count == 0)
                throw new InvalidOperationException(
                    "Mappnings-resursen innehåller 0 poster — frusen artefakt korrupt.");

            return result;
        }
    }
}
