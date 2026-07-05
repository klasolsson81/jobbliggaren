"use client";

import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  getStatusTagDataAttr,
  nextStepOf,
} from "@/lib/applications/status";
import { daysInStatus, urgencyTagFor } from "@/lib/applications/urgency";
import { setDrawerAnchor } from "@/components/applications/drawer-anchor";
import { formatDate } from "@/lib/i18n/format";
import { useApplicationActions } from "./application-actions";
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
 * Rad-klick → detaljpanelen via LÄNK-OVERLAY (CTO-bind 6, a11y): titeln är
 * radens enda <a>, sträckt över hela raden med ::after — knappzonerna ligger
 * ovanpå (z-index) så interaktiva element aldrig nästlas i ankaret (ogiltig
 * HTML). Modifierat klick (ny flik) hoppar över drawer-ankaret → fullsidan
 * (#630 PR 6-mönstret oförändrat).
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
  const format = useFormatter();
  const { pendingId, transition, openFinishDraft } = useApplicationActions();
  const { jobAd, status } = application;
  const pending = pendingId === application.id;

  const hasIdentity = jobAd != null;
  const title = hasIdentity
    ? jobAd.title
    : tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });

  const days = daysInStatus(application.lastStatusChangeAt, now);
  const urgency = urgencyTagFor(application, now);
  const urgencyLabel = ((): string | null => {
    if (urgency == null) return null;
    switch (urgency.kind) {
      case "deadline": {
        const date = formatDate(format, urgency.dateIso);
        return date != null ? tUi("urgency.deadline", { date }) : null;
      }
      case "waitDays":
        return tUi("urgency.waitDays", { days: urgency.days });
      case "sinceInterview":
        return tUi("urgency.sinceInterview", { days: urgency.days });
    }
  })();

  // Radens default-primär (design §5, prototyp-facit): utkast → "Slutför och
  // skicka"-DIALOGEN (mellansteg, §9); Ghosted → "Återaktivera" (→ Skickad);
  // annars direkt "Flytta till {nästa}"; terminala → ingen.
  const next = nextStepOf(status);
  const anchorY = (e: React.MouseEvent<HTMLButtonElement>): number =>
    e.clientY > 0 ? e.clientY : e.currentTarget.getBoundingClientRect().top;
  const defaultPrimary: RowAction | null =
    status === "Draft"
      ? {
          label: tUi("row.finishAndSend"),
          onClick: (e) => openFinishDraft(application, anchorY(e)),
        }
      : status === "Ghosted"
        ? {
            label: tUi("row.reactivate"),
            onClick: () => transition(application, "Submitted"),
          }
        : next != null
          ? {
              label: tUi("row.moveToNext", {
                status: applicationStatusLabel(t, next),
              }),
              onClick: () => transition(application, next),
            }
          : null;
  const primary = primaryAction === undefined ? defaultPrimary : primaryAction;

  return (
    <article className="jp-app jp-app--actions">
      <div className="jp-job__body">
        <h3
          className={hasIdentity ? "jp-app__title" : "jp-app__title jp-mono"}
        >
          <Link
            href={`/ansokningar/${application.id}`}
            className="jp-app__rowlink"
            // #630 PR 6 (ADR 0092 D7): record the click's viewport Y + this
            // link (the trigger) so the intercepting-route drawer opens near
            // the click (handoff §9) and returns focus here on close. href is
            // kept — a modified click (new tab/window) navigates to the full
            // page instead, so we skip the anchor for those.
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
            {title}
          </Link>
        </h3>
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
