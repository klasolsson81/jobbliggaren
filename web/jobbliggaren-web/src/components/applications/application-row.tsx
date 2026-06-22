import Link from "next/link";
import { useTranslations } from "next-intl";
import { ChevronRight } from "lucide-react";
import {
  applicationStatusLabel,
  formatSvDate,
  getStatusPillClass,
} from "@/lib/applications/status";
import type { ApplicationDto } from "@/lib/types/applications";

interface ApplicationRowProps {
  application: ApplicationDto;
}

/**
 * v3 ansökningsrad (`.jp-app`). Emitterar den DELADE `.jp-job,.jp-app`-
 * selektorn (F4-konsoliderad) → /ansokningar får IDENTISKT radchassi som
 * /jobb (HANDOVER §5.3/§9, icke-förhandlingsbar). Raden = EXAKT TVÅ
 * grid-barn (body-div + `.jp-app__actions`), prototyp-exakt (pages.jsx
 * ApplicationRow). INGEN `.jp-app__statusbadge` i raden — den 56px-
 * statusbadgen hör till MODALEN/detaljen (ApplicationDetail status-block),
 * ej raden (F5 B1, design-reviewer). Med statusbadgen borta finns ingen
 * topologi-avvikelse mot .jp-job: enda kvarvarande jp-app-unika är
 * `.jp-app__id`-chip + `.jp-app__actions` (jp-job har motsvarande).
 *
 * Hela raden är en Link till `/ansokningar/[id]` → vid soft-nav fångar
 * `@modal/(.)ansokningar/[id]` den och visar modal; hard-nav / delad länk
 * renderar fullsidan (ADR 0053, speglar F3 JobAdCard exakt). Link (ej
 * div+onClick) ger tangentbordsnåbarhet och rätt semantik utan extra ARIA
 * (CLAUDE.md §5.2 / jobbliggaren-design-a11y). Förblir Server Component
 * (server-renderas i page.tsx, passas som serialiserbar slot — F3-mönster).
 *
 * Primär identitet = jobtitel; företag separat. Fallback till mono-kort-id
 * när ingen kopplad/manuell annons finns (tillstånd 3). NEXT-datum =
 * jobAd.expiresAt (sista ansökningsdag) — REAL fält, ej v3-mock nextDate.
 */
export function ApplicationRow({ application }: ApplicationRowProps) {
  // Synchronous next-intl translator — keeps ApplicationRow a non-async RSC so
  // it remains server-renderable as a serialized slot (the page.tsx pattern)
  // and its synchronous render test stays green.
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const { jobAd } = application;

  const hasIdentity = jobAd != null;
  const title = hasIdentity
    ? jobAd.title
    : tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });

  const updatedAt = formatSvDate(application.updatedAt);
  const expiresAt = formatSvDate(jobAd?.expiresAt);

  return (
    <Link
      href={`/ansokningar/${application.id}`}
      className="jp-app"
      aria-label={
        hasIdentity
          ? tUi("row.ariaLabelWithIdentity", {
              title: jobAd.title,
              company: jobAd.company,
              status: applicationStatusLabel(t, application.status),
            })
          : tUi("row.ariaLabelFallback", {
              title,
              status: applicationStatusLabel(t, application.status),
            })
      }
    >
      <div className="jp-job__body">
        <h3
          className={
            hasIdentity ? "jp-app__title" : "jp-app__title jp-mono"
          }
        >
          {title}
        </h3>
        {hasIdentity && (
          <div className="jp-app__company">{jobAd.company}</div>
        )}
        <div className="jp-app__meta">
          <span className="jp-app__id">#{application.id.slice(0, 8)}</span>
          {updatedAt && (
            <span>
              {tUi("row.updated")} <b>{updatedAt}</b>
            </span>
          )}
          {expiresAt && (
            <span>
              {tUi("row.applyBy")} <b>{expiresAt}</b>
            </span>
          )}
        </div>
      </div>

      <div className="jp-app__actions">
        <span className={getStatusPillClass(application.status)}>
          <span className="jp-pill__dot" aria-hidden="true" />
          {applicationStatusLabel(t, application.status)}
        </span>
        <ChevronRight
          size={20}
          style={{ color: "var(--jp-ink-3)" }}
          aria-hidden="true"
        />
      </div>
    </Link>
  );
}
