import Link from "next/link";
import { useTranslations } from "next-intl";
import {
  ACTIVE_PIPELINE_STATUSES,
  applicationStatusLabel,
} from "@/lib/applications/status";
import {
  activeCount,
  countByStatus,
  statusCount,
  totalCount,
} from "@/lib/applications/pipeline-counts";
import type { PipelineGroupDto } from "@/lib/dto/applications";
import type { ApiResult } from "@/lib/dto/_helpers";

interface ApplicationSummaryProps {
  /**
   * Pipelinen som Result, inte som array. Sammanfattningen MÅSTE kunna skilja
   * "noll ansökningar" från "kunde inte hämtas" — sidan degraderar annars
   * pipeline till [] och en siffra skulle då påstå noll när sanningen är omätt.
   */
  readonly pipeline: ApiResult<PipelineGroupDto[]>;
  /**
   * Vart ankarradens länk pekar. `null` = rendera ingen länk alls.
   *
   * Obligatorisk och utan default: en utelämnad prop hade tyst löst till `/ansokningar`,
   * som ligger i `PROTECTED_PREFIXES` — på en publik yta är det en resa till
   * `/logga-in`, inte "ingen länk". Med två anropsställen totalt kostar det ingenting
   * att säga målet högt, och misstaget blir ett typfel.
   *
   * Gäst-demon (#1572) behöver båda grenarna: den har en egen ansökningsvy, men en
   * hårdkodad `/ansokningar` hade skickat en utloggad besökare till `/logga-in` via
   * proxyns `PROTECTED_PREFIXES`-grind.
   *
   * Ingen motsvarande prop för tomt-lägets `/ny-ansokan`: den grenen kräver
   * `total === 0`, och `mock-data.test.ts` pinnar minst en ansökan i var och en av
   * gästmockens fem statusar — så den är onåbar där, och en prop ingen yta kan
   * framkalla vore otestbar per konstruktion. Töms mocken någon gång faller den
   * pinnen först.
   */
  readonly linkHref: string | null;
  /**
   * Blockets namn, eller `null` för att rendera inget namn alls (#1717).
   *
   * Obligatorisk och utan default, samma regel och samma skäl som `linkHref` ovan — och båda
   * anropsställena skickar i dag `null`, vilket är ett VAL och inte en frånvaro: regeln
   * `design-reviewer` band är att ett block namnges av närmaste rubrik ovanför sig, och den här
   * sektionen har en enda innehållstyp, så dess h2 "Mina ansökningar" namnger redan blocket. En
   * h3 här vore en tautologi (design-reviewer A1/B4, 2026-09-13).
   *
   * Propen finns ändå, och det är avsiktligt: de tre sammanfattningarna delar en grammatik, och
   * ett gemensamt kontrakt gör frånvaron läsbar vid anropsstället i stället för osynlig. Blir
   * sektionen någon gång tvåtypad är rubriken en prop bort, inte en refaktorering.
   */
  readonly heading: string | null;
}

/**
 * Stående tillstånd över "Mina ansökningar" på Översikt (#1548).
 *
 * Notiserna är åtgärdsdrivna — en ansökan syns först när den legat länge nog
 * att förtjäna en påminnelse — så ett konto med levande ansökningar läste som
 * tomt. Den här raden svarar på "hur många har jag, och var ligger de" utan
 * att kopiera /ansokningar operativa stegrail hit.
 *
 * Ankarraden delar nyckel med tavlans toolbar (applications.ui.counts): {count}
 * räknas över alla tio statusar, {active} över ACTIVE_PIPELINE_STATUSES. Samma
 * mening betyder alltså samma sak på båda ytorna, för att båda läser samma två
 * SSOT — inte för att strängen råkar vara densamma.
 *
 * De fyra terminala statusarna rullas ihop till EN post i stället för fyra
 * rader: detaljen bor på /ansokningar, och utan posten hade ett konto med bara
 * avslutade ansökningar renderat sex nollor, vilket är samma falska tomhet som
 * issuet handlar om.
 */
export function ApplicationSummary({
  pipeline,
  linkHref,
  heading,
}: ApplicationSummaryProps) {
  const t = useTranslations("oversikt.summary");
  const tEnum = useTranslations("applications.enums");
  const tCounts = useTranslations("applications.ui");

  // Paritet med de två syskonen: samma villkor, samma klass, samma plats. Med `heading={null}`
  // på båda anropsställena renderar varje gren nedan exakt vad den renderade före #1717.
  const headingNode =
    heading !== null ? (
      <h3 className="jp-appsummary__heading">{heading}</h3>
    ) : null;

  if (pipeline.kind !== "ok") {
    if (headingNode === null) {
      return (
        <p className="jp-appsummary jp-appsummary--unavailable">
          {t("unavailable")}
        </p>
      );
    }
    return (
      <div className="jp-appsummary jp-appsummary--unavailable">
        {headingNode}
        <p style={{ margin: 0 }}>{t("unavailable")}</p>
      </div>
    );
  }

  const counts = countByStatus(pipeline.data);
  const total = totalCount(counts);

  if (total === 0) {
    return (
      <div className="jp-appsummary jp-appsummary--empty">
        {headingNode}
        <p className="jp-appsummary__emptytitle">{t("emptyTitle")}</p>
        <p className="jp-appsummary__emptybody">{t("emptyBody")}</p>
        {/* Betonad men INTE solid: en-primär-per-skärm är redan spenderad på
            sektionens åtgärdskort. `--emphasis` är husets ratificerade nivå
            under den (DESIGN.md §6, #1373). */}
        <Link className="jp-btn jp-btn--emphasis" href="/ny-ansokan">
          {t("emptyCta")}
        </Link>
      </div>
    );
  }

  const active = activeCount(counts);
  // De fyra terminala = alla tio minus de sex aktiva. Uttryckt som differens
  // just för att det inte ska finnas en andra lista att drifta ifrån.
  const terminal = total - active;

  return (
    <div className="jp-appsummary">
      {headingNode}
      <p className="jp-appsummary__anchor">
        <span className="jp-appsummary__totals tabular-nums">
          {tCounts("counts.totalWithActive", { count: total, active })}
        </span>
        {linkHref !== null && (
          <Link className="jp-appsummary__link" href={linkHref}>
            {t("link")}
          </Link>
        )}
      </p>

      <ul className="jp-appsummary__steps" aria-label={t("stepsAriaLabel")}>
        {ACTIVE_PIPELINE_STATUSES.map((status) => {
          const count = statusCount(counts, status);
          return (
            <li
              key={status}
              className="jp-appsummary__step"
              data-empty={count === 0 ? "true" : undefined}
            >
              <span className="jp-appsummary__name">
                {applicationStatusLabel(tEnum, status)}
              </span>
              <span className="jp-appsummary__num tabular-nums">{count}</span>
            </li>
          );
        })}
        <li
          className="jp-appsummary__step"
          data-terminal="true"
          data-empty={terminal === 0 ? "true" : undefined}
        >
          <span className="jp-appsummary__name">
            {tCounts("counts.terminalGroup")}
          </span>
          <span className="jp-appsummary__num tabular-nums">{terminal}</span>
        </li>
      </ul>
    </div>
  );
}
