"use client";

import { useId, useRef } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { applicationStatusLabel } from "@/lib/applications/status";
import { daysInStatus, urgencyTagFor } from "@/lib/applications/urgency";
import { adIdentityOf } from "./ad-identity";
import { useApplicationPending } from "./application-actions";
import { useUrgencyLabel } from "./use-urgency-label";
import { StatusMenu } from "./status-menu";
import type { ApplicationDto } from "@/lib/dto/applications";

interface ApplicationBoardCardProps {
  application: ApplicationDto;
  /** Referens-"nu" från page.tsx (#336-determinism), aldrig new Date() här. */
  now: Date;
  /** Sant → kompakt terminal-zon-kort (mindre padding, roll+subline på en rad). */
  compact?: boolean;
  /** Sant medan detta kort dras (board-lokal `dragId`) → opacity 0.4. */
  isDragging: boolean;
  /** Board-callbacks: registrera/avregistrera drag-källan (dragId + overCol). */
  onDragStart: () => void;
  onDragEnd: () => void;
}

/**
 * Tavla-kort (#630 PR 8, design §6) — kompakt, `cursor: grab`, HTML5-draggable.
 * Roll (max 2 rader) + företag + fotrad "N DGR" (mono) + ev. bråttom-tagg.
 * Kolumnen ÄR statusen, så kortet visar ingen status-tagg (till skillnad från
 * Lista-raden).
 *
 * Interaktion (design §6 "Klick öppnar detaljpanelen; drag flyttar"):
 *  - Klick → detaljpanelen via LÄNK-OVERLAY (samma a11y-mönster som
 *    ApplicationRow, CTO-bind 6): rolltiteln är kortets enda <a>, sträckt över
 *    kortet med ::after; StatusMenu ligger z-lyft ovanpå så interaktiva element
 *    aldrig nästlas i ankaret (giltig HTML). `draggable={false}` på länken så
 *    kortets (article) drag är källan, inte ankarets egen länk-drag.
 *  - Drag → statusbyte (boardet äger den optimistiska flytten). En 250ms
 *    `justDragged`-guard hindrar att släppet också öppnar detaljpanelen.
 *  - StatusMenu (samma som Lista-raden) = tangentbords-/no-drag-vägen (WCAG
 *    2.1.1 Keyboard + 2.5.7 Dragging Movements, CTO-bind A11y) — obligatorisk.
 */
export function ApplicationBoardCard({
  application,
  now,
  compact = false,
  isDragging,
  onDragStart,
  onDragEnd,
}: ApplicationBoardCardProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const contextId = useId();
  const justDraggedRef = useRef(false);
  // Tavla-kortet är inget d4-perf-mål (korten är inte memo-lindade och bara en vy
  // är monterad åt gången), men dess StatusMenu kräver `pending`-propet. Kortet
  // läser Set:et direkt: nu re-renderar hela kortet vid ett byte (förr bara dess
  // StatusMenu-barn) — grövre men försumbart, och slipper en prop-tråd genom
  // BoardColumn.
  const pending = useApplicationPending().has(application.id);

  const { jobAd, status } = application;
  // #892: strukturell identitet + borttagen-markör (lockstep med List-raden).
  const { adRemoved, title: adTitle, company: adCompany } = adIdentityOf(jobAd);
  const hasIdentity = adTitle != null;
  const title =
    adTitle ?? tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });

  const days = daysInStatus(application.lastStatusChangeAt, now);
  const daysLabel = days != null ? tUi("board.daysShort", { days }) : null;
  const urgency = urgencyTagFor(application, now);
  const urgencyLabel = useUrgencyLabel(urgency);

  return (
    <article
      className="jp-board-card"
      data-compact={compact || undefined}
      data-dragging={isDragging || undefined}
      draggable
      onDragStart={(event) => {
        event.dataTransfer.setData("text/plain", application.id);
        event.dataTransfer.effectAllowed = "move";
        onDragStart();
      }}
      onDragEnd={() => {
        // Släppet fyrar ett click på drag-källan — guarda ~250ms så
        // detaljpanelen inte öppnas direkt efter en flytt.
        justDraggedRef.current = true;
        window.setTimeout(() => {
          justDraggedRef.current = false;
        }, 250);
        onDragEnd();
      }}
    >
      <div className="jp-board-card__body">
        <h3
          className={
            hasIdentity
              ? "jp-board-card__role"
              : "jp-board-card__role jp-mono"
          }
        >
          <Link
            href={`/ansokningar/${application.id}`}
            className="jp-board-card__link"
            draggable={false}
            onClick={(event) => {
              // Ett avslutat drag får inte tolkas som klick — soft-nav
              // (route-modalen) ska bara öppnas av ett äkta klick.
              if (justDraggedRef.current) {
                event.preventDefault();
              }
            }}
            aria-describedby={contextId}
          >
            {title}
          </Link>
        </h3>
        {/* Länknamnet = rolltiteln; företag + status bärs som BESKRIVNING via
            aria-describedby (WCAG 2.4.6 — h3 förblir ren rolltitel i rotorn). */}
        <span id={contextId} className="sr-only">
          {adCompany != null
            ? `${adCompany}, ${applicationStatusLabel(t, status)}`
            : applicationStatusLabel(t, status)}
          {adRemoved ? `, ${tUi("adRemoved.tag")}` : null}
        </span>
        {compact ? (
          <div className="jp-board-card__subline">
            {adCompany != null && <span>{adCompany}</span>}
            {adCompany != null && daysLabel != null && (
              <span aria-hidden="true"> · </span>
            )}
            {daysLabel != null && (
              <span className="jp-board-card__days">{daysLabel}</span>
            )}
          </div>
        ) : (
          adCompany != null && (
            <div className="jp-board-card__company">{adCompany}</div>
          )
        )}
        {adRemoved && (
          <div className="jp-board-card__subline">
            <span className="jp-tag jp-tag--neutral">{tUi("adRemoved.tag")}</span>
          </div>
        )}
      </div>

      {!compact && (
        <div className="jp-board-card__foot">
          {daysLabel != null && (
            <span className="jp-board-card__days jp-mono">{daysLabel}</span>
          )}
          {urgencyLabel != null && urgency != null && (
            <span className="jp-tag" data-urgency={urgency.variant}>
              {urgencyLabel}
            </span>
          )}
        </div>
      )}

      {/* Tangentbords-/no-drag-vägen (CTO-bind A11y) — z-lyft ovanför
          länk-overlay:en så menyn inte nästlas i ankaret. */}
      <div className="jp-board-card__menu">
        <StatusMenu application={application} pending={pending} />
      </div>
    </article>
  );
}
