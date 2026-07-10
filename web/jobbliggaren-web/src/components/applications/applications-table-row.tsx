"use client";

import { useId } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  getStatusTagDataAttr,
  isWaitingSignal,
} from "@/lib/applications/status";
import { daysInStatus } from "@/lib/applications/urgency";
import { latestEventOf } from "@/lib/applications/latest-event";
import { formatDate } from "@/lib/i18n/format";
import { setDrawerAnchor } from "@/components/applications/drawer-anchor";
import { useApplicationActions } from "./application-actions";
import { useRowActions } from "./use-row-actions";
import { StatusMenu } from "./status-menu";
import type { ApplicationDto } from "@/lib/dto/applications";

interface ApplicationsTableRowProps {
  application: ApplicationDto;
  /** Server-beräknad referenstidpunkt (#336-determinism), trädad in per rad. */
  now: Date;
  selected: boolean;
  onToggleSelect: (id: string) => void;
}

/**
 * En rad i Tabell-vyn (#630 PR 10, design §7) — volymvyn: en tät `<tr>` som
 * skannas kolumnvis, inte handlingszons-raden (Lista) eller kanban-kortet (Tavla).
 *
 * Rad-klick → detaljpanelen via LÄNK-OVERLAY (samma mönster som `application-row`,
 * CTO-bind 6 / a11y): rolltiteln är radens ENDA <a>, sträckt över hela raden via
 * `::after` (offset-parent = den position:relative-satta `<tr>`). Checkbox,
 * StatusMenu och "Nästa steg"-länken lyfts ovanpå med `z-index` så inget
 * interaktivt element nästlas i ankaret (ogiltig HTML). Ett modifierat klick
 * (ny flik) hoppar över drawer-ankaret → fullsidan (#630 PR 6-mönstret).
 */
export function ApplicationsTableRow({
  application,
  now,
  selected,
  onToggleSelect,
}: ApplicationsTableRowProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();
  const { pendingIds } = useApplicationActions();
  const { defaultPrimaryFor } = useRowActions();

  const { jobAd, status } = application;
  const pending = pendingIds.has(application.id);
  const contextId = useId();

  const hasIdentity = jobAd != null;
  const title = hasIdentity
    ? jobAd.title
    : tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });
  const roleForLabel = hasIdentity ? jobAd.title : application.id.slice(0, 8);

  const days = daysInStatus(application.lastStatusChangeAt, now);
  const waiting = isWaitingSignal(application.attentionSignal);

  const event = latestEventOf(application);
  const eventDate = formatDate(format, event.at) ?? "";
  const eventLabel =
    event.kind === "FollowUpLogged"
      ? tUi("table.lastEventFollowUp")
      : tUi(`table.reached${event.toStatus}`);

  const nextStep = defaultPrimaryFor(application);

  return (
    <tr className="jp-apptable__row" data-selected={selected || undefined}>
      <td className="jp-apptable__cell jp-apptable__cell--check">
        <input
          type="checkbox"
          className="jp-apptable__check"
          checked={selected}
          onChange={() => onToggleSelect(application.id)}
          aria-label={tUi("table.selectRowAriaLabel", { role: roleForLabel })}
        />
      </td>

      <td className="jp-apptable__cell jp-apptable__cell--role">
        <Link
          href={`/ansokningar/${application.id}`}
          className="jp-apptable__rowlink"
          // #630 PR 6 (ADR 0092 D7): spara klickets viewport-Y + länken (triggern)
          // så drawern öppnas nära klicket och återlämnar fokus hit vid stängning.
          // href behålls — ett modifierat klick (ny flik) navigerar till fullsidan,
          // så vi hoppar över ankaret då.
          onClick={(e) => {
            if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
            setDrawerAnchor(e.clientY, e.currentTarget);
          }}
          // Länknamnet = rolltiteln; företaget bärs som beskrivning via
          // aria-describedby (WCAG 2.4.6 — ren rolltitel i rubrikrotorn).
          aria-describedby={hasIdentity ? contextId : undefined}
        >
          {title}
        </Link>
        {hasIdentity && (
          <>
            <span id={contextId} className="sr-only">
              {jobAd.company}
            </span>
            <span className="jp-apptable__company" aria-hidden="true">
              {jobAd.company}
            </span>
          </>
        )}
      </td>

      <td className="jp-apptable__cell jp-apptable__cell--status">
        <div className="jp-apptable__statuszone">
          <span className="jp-tag" data-tag={getStatusTagDataAttr(status)}>
            {applicationStatusLabel(t, status)}
          </span>
          <StatusMenu application={application} />
        </div>
      </td>

      <td className="jp-apptable__cell jp-apptable__cell--step">
        {days != null ? (
          <span
            className="jp-apptable__step"
            data-waiting={waiting || undefined}
          >
            {tUi("table.sinceStep", { days })}
          </span>
        ) : (
          <span className="jp-apptable__muted" aria-hidden="true">
            {"–"}
          </span>
        )}
      </td>

      <td className="jp-apptable__cell jp-apptable__cell--event">
        <span className="jp-apptable__event">
          <span className="jp-mono jp-apptable__eventdate">{eventDate}</span>
          <span className="jp-apptable__eventsep" aria-hidden="true">
            {" · "}
          </span>
          <span className="jp-apptable__eventlabel">{eventLabel}</span>
        </span>
      </td>

      <td className="jp-apptable__cell jp-apptable__cell--next">
        {nextStep != null && (
          <span className="jp-apptable__nextzone">
            <button
              type="button"
              className="jp-apptable__nextlink"
              disabled={pending}
              onClick={nextStep.onClick}
            >
              {nextStep.label}
            </button>
          </span>
        )}
      </td>
    </tr>
  );
}
