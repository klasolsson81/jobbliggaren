"use client";

import { memo, useId } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  getStatusTagDataAttr,
  isWaitingSignal,
} from "@/lib/applications/status";
import { daysInStatus, urgencyTagFor } from "@/lib/applications/urgency";
import { latestEventLabelKey, latestEventOf } from "@/lib/applications/latest-event";
import { formatDate } from "@/lib/i18n/format";
import { adIdentityOf } from "./ad-identity";
import { useRowActions } from "./use-row-actions";
import { useUrgencyLabel } from "./use-urgency-label";
import { StatusMenu } from "./status-menu";
import type { ApplicationDto } from "@/lib/dto/applications";

/** Explicit rad-CTA (kökortets urgens-CTA override:ar radens default, design §11). */
export interface RowAction {
  label: string;
  onClick: (event: React.MouseEvent<HTMLButtonElement>) => void;
}

interface ApplicationRowProps {
  application: ApplicationDto;
  /**
   * "Nu" beräknas EN gång i page.tsx och trädas in per rad (CTO-bind #336) så
   * tids-deriveringarna är deterministiska och testbara med injicerat datum
   * — undviker date-flake-klassen (reference_oversikt_test_dayofmonth_flake).
   */
  now: Date;
  /**
   * Pågår ett statusbyte på DENNA rad? Trådas ned från vy-containern (StatusSection
   * / AttentionQueue som läser pendingIds-Set:et) — raden prenumererar aldrig själv
   * på Set:et, så memo(ApplicationRow) skippar den vid andra raders byten (d4).
   */
  pending: boolean;
  /**
   * Primär-knappen i handlingszonen. `undefined` → radens default ("Flytta
   * till {nästa}" / "Slutför och skicka" för utkast / "Återaktivera" för
   * Ghosted; terminala rader får ingen). Kökortet skickar §11-urgens-CTA:n.
   * `null` → ingen primär-knapp.
   */
  primaryAction?: RowAction | null;
  /** Sekundär-knapp (endast kökortens §11-sekundär, t.ex. "Markera Ghosted"). */
  secondaryAction?: RowAction | null;
  /** "Byt status ▾"-menyn (design §5). Kökortet stänger av den (prototyp-facit). */
  showStatusMenu?: boolean;
}

/**
 * 2a-ansökningsraden (#630 PR 7, #780 PR-4 "kort-native", design §5) — kortet
 * adopterar Tabell-vyns fältordning så Lista och Tabell läser konsistent
 * (Stage-M-audit 2026-07-10, Klas live-pick Variant C 2026-07-11).
 *
 * 2-zons-grid `minmax(0,1fr) auto` via den ADDITIVA `.jp-app--actions`-modifiern
 * (bas-chassit `.jp-job,.jp-app` orört — gäst delar; PR5-bind E / CTO-bind 6):
 *
 *   1. Vänster (body): roll + företag, sedan meta-raden [ev. §11-bråttom-tagg +
 *      statustagg + "N dagar i steget" med `data-waiting`-varning] och en
 *      senaste-händelse-rad ("{händelse} den {datum}", FE-härledd, notistext
 *      GDPR-blockad — `latestEventOf`, delad med Tabellen).
 *   2. Höger (actions): primär rad-knapp + fulltext "Byt status ▾"-meny.
 *
 * Rad-klick → detaljmodalen via LÄNK-OVERLAY (CTO-bind 6, a11y): titeln är
 * radens enda <a>, sträckt över hela raden med ::after — handlingszonen ligger
 * ovanpå (z-index) så interaktiva element aldrig nästlas i ankaret (ogiltig
 * HTML). Meta-radens taggar/dagar är icke-interaktiva → får ligga under
 * overlayn (klick = öppna raden). Soft-nav öppnar den centrerade route-modalen
 * (ADR 0053); ett modifierat klick (ny flik) når fullsidan via href.
 */
export const ApplicationRow = memo(function ApplicationRow({
  application,
  now,
  pending,
  primaryAction,
  secondaryAction,
  showStatusMenu = true,
}: ApplicationRowProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();
  const { defaultPrimaryFor } = useRowActions();
  const { jobAd, status } = application;
  const contextId = useId();

  // #892: strukturell identitet — en raderad annons bär bevarad snapshot-
  // identitet (eller TOM identitet utan snapshot) + status "Erased"; markören
  // nedan är andra halvan av fixen (identitet utan dödssignal ser levande ut).
  const { adRemoved, title: adTitle, company: adCompany } = adIdentityOf(jobAd);
  const hasIdentity = adTitle != null;
  const title =
    adTitle ?? tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });

  const days = daysInStatus(application.lastStatusChangeAt, now);
  // "I steget"-varningen keyar på attention-signalen (firande väntesignal ≠
  // OfferAwaitingReply), ALDRIG en klient-dagströskel — samma facit som Tabellen.
  const waiting = isWaitingSignal(application.attentionSignal);
  const urgency = urgencyTagFor(application, now);
  // Delad SSOT med Tavla-kortet (DRY, PR 8) — tidigare en inline-IIFE här.
  const urgencyLabel = useUrgencyLabel(urgency);

  // Senaste händelse (Tabell-facit, #630 PR 10): FE-skalär ur list-DTO:n, ingen
  // BE-projektion. Uppföljningens fritext är PII (DEK-gräns) och surfas ALDRIG —
  // en uppföljning bär bara den generiska "Uppföljning loggad"-etiketten.
  const event = latestEventOf(application);
  const eventDate = formatDate(format, event.at);
  const eventLabel = tUi(latestEventLabelKey(event));
  const eventLine =
    eventDate != null
      ? tUi("row.latestEvent", { event: eventLabel, date: eventDate })
      : eventLabel;

  // Radens default-primär härleds i den delade `useRowActions`-hooken (DRY,
  // #630 PR 10 / senior-cto-advisor Fork 4) så Tabell-vyns "Nästa steg" delar
  // exakt samma mappning. Kökortet override:ar via `primaryAction` (§11-urgens).
  const primary =
    primaryAction === undefined ? defaultPrimaryFor(application) : primaryAction;

  return (
    <article className="jp-app jp-app--actions">
      <div className="jp-job__body">
        <h3
          className={hasIdentity ? "jp-app__title" : "jp-app__title jp-mono"}
        >
          <Link
            href={`/ansokningar/${application.id}`}
            className="jp-app__rowlink"
            // Soft-nav öppnar den centrerade route-modalen (ADR 0053);
            // modifierat klick (ny flik/fönster) når fullsidan via href.
            // Länknamnet = rolltiteln (synlig text); företag + status är
            // BESKRIVNING via aria-describedby — h3-rubriken förblir ren
            // rolltitel i rubrikrotorn (design-reviewer Minor 5, WCAG 2.4.6).
            aria-describedby={contextId}
          >
            {title}
          </Link>
        </h3>
        <span id={contextId} className="sr-only">
          {adCompany != null
            ? `${adCompany}, ${applicationStatusLabel(t, status)}`
            : applicationStatusLabel(t, status)}
          {adRemoved ? `, ${tUi("adRemoved.tag")}` : null}
        </span>
        {adCompany != null && (
          <div className="jp-app__company">{adCompany}</div>
        )}

        <div className="jp-app__metaline">
          {adRemoved && (
            <span className="jp-tag jp-tag--neutral">{tUi("adRemoved.tag")}</span>
          )}
          {urgencyLabel != null && urgency != null && (
            <span className="jp-tag" data-urgency={urgency.variant}>
              {urgencyLabel}
            </span>
          )}
          <span className="jp-tag" data-tag={getStatusTagDataAttr(status)}>
            {applicationStatusLabel(t, status)}
          </span>
          {days != null && (
            <span className="jp-app__days" data-waiting={waiting || undefined}>
              {tUi("row.daysInStep", { days })}
            </span>
          )}
        </div>
        <div className="jp-app__eventline">{eventLine}</div>
      </div>

      <div className="jp-app__actions jp-app__actions--row">
        {primary != null && (
          <button
            type="button"
            className="jp-rowbtn jp-rowbtn--emphasis"
            disabled={pending}
            onClick={primary.onClick}
          >
            {primary.label}
          </button>
        )}
        {secondaryAction != null && (
          <button
            type="button"
            className="jp-rowbtn"
            disabled={pending}
            onClick={secondaryAction.onClick}
          >
            {secondaryAction.label}
          </button>
        )}
        {showStatusMenu && (
          <StatusMenu application={application} pending={pending} />
        )}
      </div>
    </article>
  );
});
