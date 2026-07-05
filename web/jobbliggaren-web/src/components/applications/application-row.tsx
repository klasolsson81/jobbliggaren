"use client";

import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { ChevronRight } from "lucide-react";
import {
  applicationStatusLabel,
  getStatusTagDataAttr,
} from "@/lib/applications/status";
import { setDrawerAnchor } from "@/components/applications/drawer-anchor";
import { formatDate } from "@/lib/i18n/format";
import { daysSince } from "@/lib/i18n/relative-time";
import type { ApplicationDto } from "@/lib/dto/applications";

interface ApplicationRowProps {
  application: ApplicationDto;
  /**
   * "Nu" beräknas EN gång i page.tsx och trädas in per rad (CTO-bind #336) så
   * den relativa tids-taggen är deterministisk och testbar med injicerat datum
   * — undviker date-flake-klassen (reference_oversikt_test_dayofmonth_flake).
   */
  now: Date;
}

/**
 * v3 ansökningsrad (`.jp-app`). Emitterar den DELADE `.jp-job,.jp-app`-
 * selektorn (F4-konsoliderad) → /ansokningar får IDENTISKT radchassi som
 * /jobb (HANDOVER §5.3/§9, icke-förhandlingsbar). Raden = EXAKT TVÅ
 * grid-barn (body-div + `.jp-app__actions`), prototyp-exakt (pages.jsx
 * ApplicationRow). INGEN `.jp-app__statusbadge` i raden — den 56px-
 * statusbadgen hör till MODALEN/detaljen (ApplicationDetail status-block),
 * ej raden (F5 B1, design-reviewer).
 *
 * #336 slice 1 (Klas-beslut + design-reviewer-bind 2026-06-28): de tre
 * inkonsekventa behandlingarna (rundad `.jp-pill`-status + lös "Uppdaterad
 * {date}" + lös "Sök senast {date}") ersätts av ETT kvadratiskt tagg-template
 * i linje med /jobb:s `.jp-tag`:
 *   - Status förblir sitt eget skannbara element i höger-kolumnen
 *     (`.jp-app__actions`) men som en KVADRATISK färgkodad status-tagg
 *     (`[data-tag="status-*"]`) i stället för den rundade `.jp-pill`. Färg +
 *     textetikett (WCAG 1.4.1 — inte färg-enbart). Ingen dot (likt /jobb-taggen).
 *   - En relativ tids-tagg i meta-raden (neutral `.jp-tag`): "Skickad för X
 *     dagar sedan" för ALLA post-submit-tillstånd (ankrat på appliedAt);
 *     "Uppdaterad för X sedan" för Draft (ankrat på updatedAt).
 *   - "Sök senast {date}" visas ENBART för Draft (sista ansökningsdag är bara
 *     relevant innan du sökt; efter inskickad är den irrelevant).
 *
 * Hela raden är en Link till `/ansokningar/[id]` → vid soft-nav fångar
 * `@modal/(.)ansokningar/[id]` den och visar en höger-DRAWER (#630 PR 6, ADR
 * 0092 D7); hard-nav / delad länk renderar fullsidan (ADR 0053, speglar F3
 * JobAdCard exakt). #630 PR 5 (ADR
 * 0092 D2): raden är nu en KLIENTkomponent — ön (ApplicationsPipeline) tar emot
 * serialiserbar data (`PipelineGroupDto[]` + `nowIso`) och renderar raden
 * direkt; en klient-ö kan inte importera en Server Component, och rowSlots-
 * slot-mappen (eece124-workaround) är därmed borta. Radens 2a-redesign (3-zons-
 * grid, statusmeny, "Flytta till"-knapp) landar i PR 7 med sitt maskineri.
 *
 * Primär identitet = jobtitel; företag separat. Fallback till mono-kort-id
 * när ingen kopplad/manuell annons finns (tillstånd 3).
 */
export function ApplicationRow({ application, now }: ApplicationRowProps) {
  // Synchronous next-intl client hooks — the row is a client component (#630
  // PR 5) rendered by the client island straight from serialized DTO data; the
  // synchronous render keeps its render test simple and deterministic.
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();
  const { jobAd, status } = application;

  const hasIdentity = jobAd != null;
  const title = hasIdentity
    ? jobAd.title
    : tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });

  const isDraft = status === "Draft";

  // Relativ tids-tagg. Draft → "Uppdaterad …" ankrad på updatedAt; post-submit
  // → "Skickad …" ankrad på appliedAt. Defensivt: om en icke-Draft saknar
  // appliedAt (deploy-skew / dataglapp) faller vi tillbaka på updatedAt så
  // taggen aldrig blir tom. days clampas till >=0 (negativ framtid → "i dag").
  const relativeAnchor = isDraft
    ? application.updatedAt
    : application.appliedAt ?? application.updatedAt;
  const relativeDays = Math.max(0, daysSince(relativeAnchor, now));
  const relativeLabel = isDraft
    ? tUi("row.relativeDraftUpdated", { days: relativeDays })
    : tUi("row.relativeApplied", { days: relativeDays });

  // Sista ansökningsdag — ENBART för Draft (Klas-beslut: irrelevant efter
  // inskickad ansökan).
  const expiresAt = isDraft ? formatDate(format, jobAd?.expiresAt) : null;

  return (
    <Link
      href={`/ansokningar/${application.id}`}
      className="jp-app"
      // #630 PR 6 (ADR 0092 D7): record the click's viewport Y + this row (the
      // trigger) so the intercepting-route drawer opens near the click (handoff
      // §9) and returns focus here on close. href is kept — a modified click
      // (new tab/window) navigates to the full page instead, so we skip the
      // anchor for those (the drawer never opens).
      onClick={(e) => {
        if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        setDrawerAnchor(e.clientY, e.currentTarget);
      }}
      aria-label={
        hasIdentity
          ? tUi("row.ariaLabelWithIdentity", {
              title: jobAd.title,
              company: jobAd.company,
              status: applicationStatusLabel(t, status),
            })
          : tUi("row.ariaLabelFallback", {
              title,
              status: applicationStatusLabel(t, status),
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
          <span className="jp-tag jp-tag--neutral">{relativeLabel}</span>
          {expiresAt && (
            <span>
              {tUi("row.applyBy")} <b>{expiresAt}</b>
            </span>
          )}
        </div>
      </div>

      <div className="jp-app__actions">
        <span className="jp-tag" data-tag={getStatusTagDataAttr(status)}>
          {applicationStatusLabel(t, status)}
        </span>
        <ChevronRight
          size={20}
          className="text-text-tertiary"
          aria-hidden="true"
        />
      </div>
    </Link>
  );
}
