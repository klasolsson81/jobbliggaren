import { useTranslations } from "next-intl";
import { StatusPill } from "@/components/ui/status-pill";
import { CvProfileToggle } from "@/components/resumes/cv-profile-toggle";
import { CvCriterionVerdict } from "@/components/resumes/cv-criterion-verdict";
import { CvFindingStatusControl } from "@/components/resumes/cv-finding-status-control";
import { bandLabel, categoryLabel } from "@/lib/resumes/review-labels";
import type {
  CvReviewDto,
  CvReviewCategoryDto,
  CvCriterionVerdictDto,
  RenderProfile,
} from "@/lib/dto/parsed-resume";

/**
 * CV-granskningspanel (F4-9). RSC. Tre lager top-down — rätt ORDNING sedan
 * REVIEW-IA-REDESIGN B, rätt VIKT sedan #1062 Q1:
 *   1. "Att åtgärda" — alla åtgärdbara verdikt (Underkänt/Delvis) över ALLA
 *      kategorier, severitets-sorterade. Sidans huvudinnehåll: egen h2, full
 *      bredd, inget kort-krom.
 *   2. "Bedömning per dimension" — EN rad per kategori (band med sin täckning +
 *      en demoterad räknarrad), med de Godkända bakom en disclosure.
 *   3. "Ej bedömt" — en kollapsad, lågprioriterad disclosure längst ned.
 *
 * Vikten mättes: kategorikorten tog 2059px av 3577px (58 %) på ett svagt CV och
 * 2225px av 3396px (66 %) på ett rent, och de 15 raderna i dem var 15 `Godkänt`
 * — verdikt som redan är avklarade — medan de två åtgärdbara fynden fick ~450px.
 * Formen byggdes när granskaren var ett av flera CV-verktyg; efter ADR 0112 ÄR
 * den produkten, och då är referensmaterialet det som ska kollapsa.
 *
 * Ingen opak totalpoäng (Goodhart, §5/ADR 0074) — band + räknare per dimension =
 * förklarbart. Honesty-invarianten (ADR 0074): "Ej bedömt" får demoteras men
 * ALDRIG döljas eller om-etiketteras som bedömt — och sedan #1062 B1 inte heller
 * renderas som en LÅG grad: en kategori utan bedömda kriterier bär inget band alls
 * (se `CategoryBand`). Kollapsen ändrar inget i den invarianten: ett `<details>`
 * är demotering, inte döljande — innehållet står i DOM:en och är tangentbords-
 * nåbart. När `review` är null (granskningen kunde inte laddas) degraderas vyn
 * civilt — parse-vyn står kvar, granskningen ersätts av en notis (sidan 404:ar
 * aldrig på detta).
 */

/**
 * Vilken CV-granskningen visas för (Q7(e) "canonical variant"). `parsed` =
 * import-stagingen (`/cv/granska/{parsedId}`, ingen statusledger). `canonical` =
 * en befordrad Resume (`/cv/{resumeId}/granska`) som bär finding-statusledgern
 * och därför får per-anmärkning statuskontroller.
 */
export type CvReviewTarget =
  | { kind: "parsed"; parsedId: string }
  | { kind: "canonical"; resumeId: string };

/** Route-basen profil-växeln länkar inom, härledd ur target:en. */
function toggleBasePath(target: CvReviewTarget): string {
  return target.kind === "canonical"
    ? `/cv/${target.resumeId}/granska`
    : `/cv/granska/${target.parsedId}`;
}

/** Severitets-rang för "Att åtgärda"-sorteringen: Underkänt (Fail) före Delvis
 * (Warn). Endast åtgärdbara verdikt sorteras här. */
const SEVERITY_RANK: Record<"Fail" | "Warn", number> = { Fail: 0, Warn: 1 };

/** Antalet kriterier i kategorin som faktiskt fick ett verdikt. */
function assessedIn(category: CvReviewCategoryDto): number {
  return category.passCount + category.warnCount + category.failCount;
}

/**
 * Bandet med sin TÄCKNING, eller den uttalade frånvaron av ett band (#1062 B1/M1/M2).
 *
 * Bandet är en VIKTAD poäng och räknarna under det en OVIKTAD tally, så `Toppskikt`
 * kunde stå rakt ovanför sin egen `Delvis = 2` utan att något markerade att de två
 * mäter olika saker (M1). Och utan nämnare var `Toppskikt` av 2 bedömda kriterier
 * typografiskt identiskt med `Toppskikt` av 10 (M2) — mätt: 3 av 4 band var lika på
 * ett svagt och ett rent CV. Bandet renderas därför aldrig utan sin täckning bredvid
 * sig, i samma block, så en skärmläsare läser dem i följd.
 *
 * M1:s residual — att pillen och räknarna inte var typografiskt åtskilda — stängs av
 * Q2, som tar räknarna ur `--text-h3`/bold/tonfärg och ned i brödtext: pillen är
 * verdiktet, raden under den är dess sammansättning, och de läser inte längre som
 * jämlikar.
 *
 * `band === null` är inte "lägsta graden" utan INGEN grad. Frånvaron skrivs ut i
 * klartext i stället för att förmedlas genom att en pill saknas — samma lärdom som
 * M4:s "Öppen"-tillstånd.
 *
 * ⚠ Tröskeln för att hålla tillbaka ett band ÖVER noll är ett Klas-/rubrikbeslut
 * (ADR 0071: trösklar hör i rubrikdatan, inte i C# och inte här). Noll är §5-
 * invarianten och kräver honom inte.
 */
function CategoryBand({
  category,
  t,
  tEnum,
}: {
  category: CvReviewCategoryDto;
  t: ReturnType<typeof useTranslations<"resumes">>;
  tEnum: ReturnType<typeof useTranslations<"resumes.enums">>;
}) {
  const assessed = assessedIn(category);
  const total = assessed + category.notAssessedCount;

  if (category.band === null) {
    // Meningen PÅSTÅR att inget kriterium kunde bedömas, så den grindas på DET och
    // inte på `band === null`. Backend håller de två ekvivalenta idag, men bara
    // därför att rubrikens vikter alla är > 0; en rubrikbump med en nollviktad nivå
    // ger `weightSum === 0` med bedömda kriterier kvar, och då hade sidan skrivit ut
    // ett påstående som räknarraden under motbevisar. Ett band vi inte fick, med
    // bedömda kriterier bakom sig, visar sin täckning utan pill.
    return assessed === 0 ? (
      <p className="jp-cvreview__band-unassessed">
        {t("review.band.unassessed", { count: total })}
      </p>
    ) : (
      <div className="jp-cvreview__band">
        <span className="jp-cvreview__band-coverage">
          {t("review.band.coverage", { assessed, total })}
        </span>
      </div>
    );
  }

  const band = bandLabel(tEnum, category.band);
  return (
    <div className="jp-cvreview__band">
      <StatusPill tone={band.tone}>{band.label}</StatusPill>
      <span className="jp-cvreview__band-coverage">
        {t("review.band.coverage", { assessed, total })}
      </span>
    </div>
  );
}

/**
 * Räknarna per kategori (#1062 Q2). Etikett + siffra, alltid båda — status aldrig
 * enbart färg (WCAG 1.4.1) — men MEDIET är bytt, inte informationen: fyra boxade tal
 * i `--text-h3`/bold/tonfärg är nu en rad i brödtext. Mätt: de tog den starkaste
 * typografiska positionen i varje kort medan varje siffra räknade rader sidan redan
 * visade (`rowsInCard === Godkänt` på 12 av 12 kategori/yta-par). Efter Q1:s kollaps
 * ligger de Godkända bakom en stängd disclosure, så raden räknar numera något som
 * inte står synligt bredvid den.
 *
 * Nollor undertrycks: på ett rent CV var 8 av 16 celler `0`, och en nolla är inget
 * fynd. Varje räknare som INTE är noll renderas.
 *
 * Hela raden utelämnas i det ena fallet där den bara skulle upprepa en mening ordagrant
 * ovanför sig: den obandade kategorin, där `CategoryBand` redan skriver ut "Inget av de
 * N kriterierna kunde bedömas" — samma tal, i ord. Grinden är därför DEN meningens egen
 * grind, ordagrant.
 *
 * ⚠ Den är REDUNDANT mot dagens motor, och skrivs ut ändå. `weightSum` ackumuleras bara
 * över bedömda kriterier (`CvReviewEngine`), så `assessed === 0` medför `band === null`
 * strukturellt — för vilka vikter som helst. `assessed === 0` ensamt hade alltså räckt,
 * och en mutation som tar bort första konjunkten överlever varje test i sviten. Den står
 * kvar därför att raden undertrycks på grund av att MENINGEN renderas, inte på grund av
 * att inget bedömdes; en grind som bara råkade sammanfalla hade kunnat driva isär tyst
 * om kopplingen i motorn ändrades. Vad konjunkten INTE skyddar mot är en nollviktad
 * rubriknivå — den ger `band === null && assessed > 0`, och där renderar båda formerna
 * raden ändå.
 */
function CategoryTally({
  category,
  t,
}: {
  category: CvReviewCategoryDto;
  t: ReturnType<typeof useTranslations<"resumes">>;
}) {
  const assessed = assessedIn(category);
  if (category.band === null && assessed === 0) return null;

  const counts: ReadonlyArray<{ key: string; label: string; value: number }> = [
    { key: "pass", label: t("review.counts.pass"), value: category.passCount },
    { key: "warn", label: t("review.counts.warn"), value: category.warnCount },
    { key: "fail", label: t("review.counts.fail"), value: category.failCount },
    {
      key: "notAssessed",
      label: t("review.counts.notAssessed"),
      value: category.notAssessedCount,
    },
  ];
  const present = counts.filter((count) => count.value > 0);
  if (present.length === 0) return null;

  return (
    <dl className="jp-cvreview__tally">
      {present.map((count) => (
        <div key={count.key} className="jp-cvreview__tally-item">
          <dt>{count.label}</dt>
          <dd className="jp-cvreview__tally-value">{count.value}</dd>
        </div>
      ))}
    </dl>
  );
}

/**
 * Lager 2, en kategori = EN rad (#1062 Q1 punkt 2). Namn + band-med-täckning +
 * räknarrad; de Godkända verdikten ligger bakom en disclosure, för de är
 * referensmaterial och inte produkten.
 *
 * Disclosuren renderas bara när det FINNS Godkänt att visa — en `<summary>` som
 * öppnar tomrum är en affordans som ljuger. Raden i sig är samma element i båda
 * fallen, så en kategori utan Godkänt läser som en kategori och inte som ett fel.
 *
 * Den krymper också en lucka design-reviewer mätte men inte graderade: mellan sista
 * statusknappen och "Ej bedömt"-disclosuren låg 2159px utan ett enda tab-stopp, så en
 * tangentbordsanvändare hade ingen väg att röra sig mellan kategorierna.
 *
 * ⚠ Den STÄNGS inte, och villkoret är samma villkor som två rader upp: en dimension utan
 * Godkänt får ingen disclosure och därmed inget tab-stopp. Mätt 2026-08-18 efter fixen:
 * största luckan 173px på de levererade ytorna — men det talet gäller det CV:t, och ett vars
 * dimensioner saknar Godkänt lämnar luckan öppen, och den parsade ytan är värst utsatt
 * eftersom den inte heller har statuskontroller i lager 1.
 */
function CategoryRow({
  category,
  passVerdicts,
  headingTag: HeadingTag,
  t,
  tEnum,
}: {
  category: CvReviewCategoryDto;
  passVerdicts: ReadonlyArray<CvCriterionVerdictDto>;
  /** Rangen kategorinamnet renderas på — ett steg under lagerrubrikerna, som i sin tur
   * beror på om sidans h1 äger granskningen (se `CvReviewPanel`). */
  headingTag: "h3" | "h4";
  t: ReturnType<typeof useTranslations<"resumes">>;
  tEnum: ReturnType<typeof useTranslations<"resumes.enums">>;
}) {
  return (
    <div className="jp-cvreview__dimension">
      <div className="jp-cvreview__dimension-head">
        <HeadingTag className="jp-cvreview__dimension-name">
          {categoryLabel(tEnum, category.category)}
        </HeadingTag>
        <CategoryBand category={category} t={t} tEnum={tEnum} />
      </div>

      <CategoryTally category={category} t={t} />

      {passVerdicts.length > 0 && (
        <details className="jp-cvreview__pass">
          <summary className="jp-cvreview__pass-summary">
            {t("review.passSummary", { count: passVerdicts.length })}
          </summary>
          <div className="jp-cvreview__verdicts">
            {passVerdicts.map((verdict) => (
              <CvCriterionVerdict key={verdict.criterionId} verdict={verdict} />
            ))}
          </div>
        </details>
      )}
    </div>
  );
}

export function CvReviewPanel({
  review,
  target,
  profile,
}: {
  review: CvReviewDto | null;
  target: CvReviewTarget;
  profile: RenderProfile;
}) {
  const t = useTranslations("resumes");
  const tEnum = useTranslations("resumes.enums");
  const basePath = toggleBasePath(target);

  // Rubrikrangen beror på om SIDANS h1 äger granskningen, och de två ytorna svarar olika
  // (#1062 Q1 + minor 1 + design-M2).
  //
  // På den kanoniska ytan säger h1 "Granskning av ditt CV" — panelen ÄR sidan. En egen
  // rubrik hade upprepat h1:an och skjutit ned varje lager ett steg, så att "Att åtgärda"
  // hamnade på h3 jämsides med kategorirubrikerna: precis den peer-läsning Q1 river.
  // Lagren äger därför h2 där, och regionen namnges via aria-label.
  //
  // På stagingytan handlar h1 om den importerade FILEN, och granskningen är ett block
  // bland parse-artefakter — mätt 2026-08-18 till 1295px av 3635 (36 %) utan namn i sidans
  // outline, med sina lager som jämlikar till artefakterna omkring. Där behöver panelen en egen synlig
  // h2, och lagren går ned på h3. Ett landmark-namn räcker inte: det bär bara till AT, och
  // en seende användare får ingenting.
  //
  // Det som avgör är alltså sidan, inte target:en; `target.kind` sammanfaller med den
  // frågan på båda levererade ytorna, och en härledning kan inte sättas fel av en
  // anropsplats så som en prop kan.
  const ownsPageTitle = target.kind === "canonical";
  const LayerHeading = ownsPageTitle ? "h2" : "h3";
  const CategoryHeading = ownsPageTitle ? "h3" : "h4";
  const regionProps = ownsPageTitle
    ? { "aria-label": t("review.title") }
    : { "aria-labelledby": "cvreview-title" };

  // Frånvaro-grenen bär ALLTID rubriken, på båda ytorna: den har inga lager, alltså ingen
  // rangkonflikt att lösa — och utan den står en `role="status"`-notis i en namnlös region.
  if (review === null) {
    return (
      <section className="jp-cvreview" aria-labelledby="cvreview-title">
        <h2 id="cvreview-title" className="jp-cvreview__title">
          {t("review.title")}
        </h2>
        <div className="jp-cvreview__profile">
          <CvProfileToggle basePath={basePath} profile={profile} />
        </div>
        <p className="jp-cvreview__unavailable" role="status">
          {t("review.unavailable")}
        </p>
      </section>
    );
  }

  // Lager 1 — "Att åtgärda": alla åtgärdbara verdikt över alla kategorier.
  // Kritisk-flaggan är en INTERN sortnyckel (inte en separat region): ett verdikt
  // vars criterionId finns i criticalFails sorteras överst inom sin severitet.
  const criticalIds = new Set(review.criticalFails.map((v) => v.criterionId));
  const isActionable = (
    v: CvCriterionVerdictDto,
  ): v is CvCriterionVerdictDto & { verdict: "Fail" | "Warn" } =>
    v.verdict === "Fail" || v.verdict === "Warn";

  const actionable = review.verdicts.filter(isActionable).sort((a, b) => {
    const bySeverity = SEVERITY_RANK[a.verdict] - SEVERITY_RANK[b.verdict];
    if (bySeverity !== 0) return bySeverity;
    // Inom samma severitet: kritiska först (true → 0, false → 1).
    return (
      Number(!criticalIds.has(a.criterionId)) -
      Number(!criticalIds.has(b.criterionId))
    );
  });

  // Lager 3 — "Ej bedömt": demoterade till en kollapsad disclosure längst ned.
  const notAssessed = review.verdicts.filter(
    (v) => v.verdict === "NotAssessed",
  );

  return (
    <section className="jp-cvreview" {...regionProps}>
      {!ownsPageTitle && (
        <h2 id="cvreview-title" className="jp-cvreview__title">
          {t("review.title")}
        </h2>
      )}

      <div className="jp-cvreview__profile">
        <CvProfileToggle basePath={basePath} profile={profile} />
      </div>

      {/* Täckningsberättelsen lyft (#1062 Q1 punkt 3): "17 av 35 kriterier är bedömda" är
          sidans mest bärande mening — den säger hur mycket av CV:t som faktiskt blev
          granskat — och stod på samma `--text-body-sm` som brödtexten runt den, med de
          18 obedömda 3051px längre ned. Den leder nu; hederlighetsklausulen och
          rubrikversionen stöder från raden under. */}
      <div className="jp-cvreview__coverage-block">
        <p className="jp-cvreview__coverage">
          {t("review.summary", {
            assessedCount: review.assessedCount,
            totalCount: review.totalCount,
          })}
        </p>
        <p className="jp-cvreview__coverage-note">
          {t("review.summaryNote")}{" "}
          <span className="jp-cvreview__rubric">
            {t("review.rubric", { version: review.rubricVersion })}
          </span>
        </p>
      </div>

      {/* Lager 1 — Att åtgärda. Sidans huvudinnehåll: egen h2, full bredd, inget
          kort-krom. Före #1062 Q1 bar det samma vita kort-krom som de fyra
          kategorikorten, skilt bara av `--jp-border-strong` mot deras `--jp-border`
          — vid 1px nära osynligt — så det läste som en låda bland lådor. */}
      <section
        className="jp-cvreview__todo"
        aria-labelledby="cvreview-todo-title"
      >
        <LayerHeading id="cvreview-todo-title" className="jp-cvreview__todo-title">
          {t("review.todoTitle", { count: actionable.length })}
        </LayerHeading>
        {actionable.length === 0 ? (
          // #1062 minor 3: "Inget kräver åtgärd just nu." stod ensamt medan 18 av 35
          // kriterier aldrig bedömdes — en mening som läses som ett utlåtande om HELA
          // CV:t men bara bär de bedömda. Påståendet knyts nu till sitt underlag.
          // ⚠ Vilket NÄSTA STEG som är rätt på ett rent, sparat CV är ett Klas-
          // produktbeslut (CTO Klas-carry 2), så här lagas bara den falska halvan:
          // ingen CTA läggs till.
          <p className="jp-cvreview__todo-empty">
            {t("review.todoEmpty", { assessedCount: review.assessedCount })}{" "}
            {review.totalCount > review.assessedCount &&
              t("review.todoEmptyUnassessed", {
                count: review.totalCount - review.assessedCount,
              })}
          </p>
        ) : (
          <div className="jp-cvreview__verdicts">
            {actionable.map((verdict) => (
              <CvCriterionVerdict
                key={verdict.criterionId}
                verdict={verdict}
                categoryLabel={categoryLabel(tEnum, verdict.category)}
                // Statuskontroll ENBART på den kanoniska granskningen (befordrad
                // Resume) — den parsade stagingen har ingen statusledger.
                footer={
                  target.kind === "canonical" ? (
                    <CvFindingStatusControl
                      resumeId={target.resumeId}
                      criterionId={verdict.criterionId}
                      userStatus={verdict.userStatus}
                      userStatusStaleAt={verdict.userStatusStaleAt}
                      isIgnorable={verdict.isIgnorable}
                    />
                  ) : undefined
                }
              />
            ))}
          </div>
        )}
      </section>

      {/* Lager 2 — Bedömning per dimension. En rad per kategori; de Godkända bakom
          varsin disclosure. Rubriken bär information i stället för att upprepa h1:an
          (minor 1: h1 "Granskning av ditt CV" → h2 "Granskning"). */}
      <section
        className="jp-cvreview__dimensions"
        aria-labelledby="cvreview-dimensions-title"
      >
        <LayerHeading
          id="cvreview-dimensions-title"
          className="jp-cvreview__dimensions-title"
        >
          {t("review.categoriesTitle")}
        </LayerHeading>
        {review.categories.map((category) => (
          <CategoryRow
            key={category.category}
            headingTag={CategoryHeading}
            category={category}
            passVerdicts={review.verdicts.filter(
              (verdict) =>
                verdict.category === category.category &&
                verdict.verdict === "Pass",
            )}
            t={t}
            tEnum={tEnum}
          />
        ))}
      </section>

      {/* Lager 3 — Ej bedömt (kollapsad, lågprioriterad, men aldrig dold) */}
      {notAssessed.length > 0 && (
        <details className="jp-cvreview__unassessed">
          <summary className="jp-cvreview__unassessed-summary">
            {t("review.unassessedSummary", { count: notAssessed.length })}
          </summary>
          <div className="jp-cvreview__verdicts">
            {notAssessed.map((verdict) => (
              <CvCriterionVerdict
                key={verdict.criterionId}
                verdict={verdict}
                categoryLabel={categoryLabel(tEnum, verdict.category)}
              />
            ))}
          </div>
        </details>
      )}
    </section>
  );
}
