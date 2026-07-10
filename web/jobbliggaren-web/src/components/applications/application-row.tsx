"use client";

import { useId } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  getStatusTagDataAttr,
} from "@/lib/applications/status";
import { daysInStatus, urgencyTagFor } from "@/lib/applications/urgency";
import { useApplicationActions } from "./application-actions";
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
 * 2a-ansökningsraden (#630 PR 7, design §5) — 3-zons-grid
 * `minmax(0,1fr) auto auto` via den ADDITIVA `.jp-app--actions`-modifiern
 * (bas-chassit `.jp-job,.jp-app` orört — PR5-bind E / CTO-bind 6):
 *
 *   1. Vänster: roll + företag (en rad, ellipsis).
 *   2. Info-zon: ev. bråttom-tagg (§11, data-grundad — aldrig fabricerad) +
 *      statustagg + "N dagar i steget" (list-DTO:ns `lastStatusChangeAt`).
 *   3. Handlingszon: avdelare + primär rad-knapp + "Byt status ▾"-meny.
 *      Allt till höger om avdelaren är klickbart (design §5).
 *
 * Rad-klick → detaljmodalen via LÄNK-OVERLAY (CTO-bind 6, a11y): titeln är
 * radens enda <a>, sträckt över hela raden med ::after — knappzonerna ligger
 * ovanpå (z-index) så interaktiva element aldrig nästlas i ankaret (ogiltig
 * HTML). Soft-nav öppnar den centrerade route-modalen (ADR 0053); ett
 * modifierat klick (ny flik) når fullsidan via href.
 */
export function ApplicationRow({
  application,
  now,
  primaryAction,
  secondaryAction,
  showStatusMenu = true,
}: ApplicationRowProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const { pendingIds } = useApplicationActions();
  const { defaultPrimaryFor } = useRowActions();
  const { jobAd, status } = application;
  const pending = pendingIds.has(application.id);
  const contextId = useId();

  const hasIdentity = jobAd != null;
  const title = hasIdentity
    ? jobAd.title
    : tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });

  const days = daysInStatus(application.lastStatusChangeAt, now);
  const urgency = urgencyTagFor(application, now);
  // Delad SSOT med Tavla-kortet (DRY, PR 8) — tidigare en inline-IIFE här.
  const urgencyLabel = useUrgencyLabel(urgency);

  // Radens default-primär härleds nu i den delade `useRowActions`-hooken (DRY,
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
          {hasIdentity
            ? `${jobAd.company}, ${applicationStatusLabel(t, status)}`
            : applicationStatusLabel(t, status)}
        </span>
        {hasIdentity && <div className="jp-app__company">{jobAd.company}</div>}
      </div>

      <div className="jp-app__signals">
        {urgencyLabel != null && urgency != null && (
          <span className="jp-tag" data-urgency={urgency.variant}>
            {urgencyLabel}
          </span>
        )}
        <span className="jp-tag" data-tag={getStatusTagDataAttr(status)}>
          {applicationStatusLabel(t, status)}
        </span>
        {days != null && (
          <span className="jp-app__days">
            {tUi("row.daysInStep", { days })}
          </span>
        )}
      </div>

      <div className="jp-app__actions jp-app__actions--row">
        <span className="jp-app__divider" aria-hidden="true" />
        {primary != null && (
          <button
            type="button"
            className="jp-rowbtn jp-rowbtn--primary"
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
        {showStatusMenu && <StatusMenu application={application} />}
      </div>
    </article>
  );
}
