"use client";

import { useMemo, useOptimistic, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import {
  ACTIVE_PIPELINE_STATUSES,
  applicationStatusLabel,
  getStatusVariantKey,
  PIPELINE_ORDER,
  STATUS_MENU_CLOSED_GROUP,
} from "@/lib/applications/status";
import {
  applyBoardMove,
  bucketsFromGroups,
} from "@/lib/applications/board-model";
import { transitionStatusAction } from "@/lib/actions/applications";
import { showApplicationToast } from "@/lib/applications/toast-store";
import { applicationDisplayName } from "./application-actions";
import { ApplicationBoardCard } from "./application-board-card";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

// Synliga kort per kolumn innan "Visa N fler" (design §6). Enkel konstant, ingen
// config — bara den visuella kapningen; inget döljs permanent.
const COLUMN_CARD_CAP = 4;

interface ApplicationsBoardProps {
  groups: PipelineGroupDto[];
  now: Date;
  /** Fri-textsök från kontrollraden — filtrerar korten (stegfiltret gäller ej i Tavla). */
  query: string;
}

/**
 * Tavla-vyn (#630 PR 8, design §6, ADR 0092 D1) — kanban över SAMMA
 * `PipelineGroupDto[]` som Lista (D2). Sex kolumner (aktiva steg) +
 * högerspalt med fyra terminal-mini-zoner. Drag släpper = statusbyte.
 *
 * Optimistisk flytt (Klas-bekräftad, ADR 0092 Livscykel-amendment 2026-07-06):
 * boardet äger en board-scoped `useOptimistic` som flyttar kortet till
 * målkolumnen OMEDELBART vid släpp; servern förblir SSOT — den SAMMA auditerade
 * `transitionStatusAction` persistar och `revalidatePath` rekoncilierar (nya
 * `groups` → `base` re-deriveras → overlay:en kastas). Vid fel avancerar servern
 * aldrig → overlay:en auto-återgår + error-toasten (samma toast-store som Lista).
 * Detta är den ENDA platsen med optimism — providern (Lista/StatusMenu) förblir
 * icke-optimistisk (PR 7-bind orört). Attention/tidsderivat klient-omräknas
 * ALDRIG (CTO-bind D-C).
 *
 * `transition()` från providern kan inte äga overlay-livstiden (den kör en egen
 * inre `startTransition` och är fire-and-forget), så boardet använder syskon-
 * mönstret (`drawer-status-actions`): `transitionStatusAction` +
 * `showApplicationToast` DIREKT i boardets egna `startTransition(async …)` —
 * SAMMA auditerade server-action, SAMMA toast-store, INGEN ny mutation/audit/
 * toast-väg (DRY, en auditerad transition, en ångra-toast).
 */
export function ApplicationsBoard({ groups, now, query }: ApplicationsBoardProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");

  const trimmed = query.trim().toLowerCase();
  const base = useMemo(
    () => bucketsFromGroups(groups, trimmed),
    [groups, trimmed],
  );

  const [buckets, addMove] = useOptimistic(base, applyBoardMove);
  const [, startTransition] = useTransition();

  // Board-lokal DnD-UI-state (ingen optimism): vilket kort dras, vilken kolumn
  // pekaren är över.
  const [dragId, setDragId] = useState<string | null>(null);
  const [overStatus, setOverStatus] = useState<ApplicationStatus | null>(null);

  const findApp = (id: string): ApplicationDto | null => {
    for (const status of PIPELINE_ORDER) {
      const found = buckets[status].find((application) => application.id === id);
      if (found != null) return found;
    }
    return null;
  };

  const moveCard = (application: ApplicationDto, target: ApplicationStatus) => {
    if (application.status === target) return; // samma kolumn = no-op (transition-vaktens paritet)
    startTransition(async () => {
      addMove({ id: application.id, to: target });
      const result = await transitionStatusAction(application.id, target);
      if (result.success) {
        showApplicationToast({
          kind: "statusChange",
          applicationId: application.id,
          company: applicationDisplayName(application),
          from: application.status,
          to: target,
        });
      } else {
        showApplicationToast({ kind: "error", message: result.error });
      }
    });
  };

  const dropProps = (status: ApplicationStatus) => ({
    onDragOver: (event: React.DragEvent<HTMLElement>) => {
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      if (overStatus !== status) setOverStatus(status);
    },
    onDragLeave: (event: React.DragEvent<HTMLElement>) => {
      // Bara rensa om pekaren lämnar kolumnen (inte när den går in i ett barn).
      if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
        setOverStatus((current) => (current === status ? null : current));
      }
    },
    onDrop: (event: React.DragEvent<HTMLElement>) => {
      event.preventDefault();
      const id = dragId ?? event.dataTransfer.getData("text/plain");
      setOverStatus(null);
      setDragId(null);
      if (!id) return;
      const application = findApp(id);
      if (application != null) moveCard(application, status);
    },
  });

  const cardProps = {
    dragId,
    onDragStart: (id: string) => setDragId(id),
    onDragEnd: () => {
      setDragId(null);
      setOverStatus(null);
    },
  };

  const activeCount = ACTIVE_PIPELINE_STATUSES.reduce(
    (sum, status) => sum + buckets[status].length,
    0,
  );
  const totalCount = PIPELINE_ORDER.reduce(
    (sum, status) => sum + buckets[status].length,
    0,
  );

  return (
    <div className="jp-board">
      <div className="jp-board__toolbar">
        <span className="jp-board__count">
          {tUi("board.toolbarCount", { count: totalCount, active: activeCount })}
        </span>
        <span className="jp-board__hint">{tUi("board.toolbarHint")}</span>
      </div>

      <div className="jp-board__grid">
        {ACTIVE_PIPELINE_STATUSES.map((status) => (
          <BoardColumn
            key={status}
            status={status}
            label={applicationStatusLabel(t, status)}
            variant="column"
            apps={buckets[status]}
            now={now}
            isOver={overStatus === status}
            emptyText={tUi("board.emptyColumn")}
            dropProps={dropProps(status)}
            cardProps={cardProps}
          />
        ))}

        <div className="jp-board__terminal">
          {STATUS_MENU_CLOSED_GROUP.map((status) => (
            <BoardColumn
              key={status}
              status={status}
              label={applicationStatusLabel(t, status)}
              variant="zone"
              apps={buckets[status]}
              now={now}
              isOver={overStatus === status}
              emptyText={
                status === "Accepted"
                  ? tUi("board.emptyAccepted")
                  : tUi("board.emptyZone")
              }
              dropProps={dropProps(status)}
              cardProps={cardProps}
            />
          ))}
          <p className="jp-board__ghosthint">{tUi("board.ghostHint")}</p>
        </div>
      </div>
    </div>
  );
}

interface BoardColumnProps {
  status: ApplicationStatus;
  label: string;
  variant: "column" | "zone";
  apps: ApplicationDto[];
  now: Date;
  isOver: boolean;
  emptyText: string;
  dropProps: {
    onDragOver: (event: React.DragEvent<HTMLElement>) => void;
    onDragLeave: (event: React.DragEvent<HTMLElement>) => void;
    onDrop: (event: React.DragEvent<HTMLElement>) => void;
  };
  cardProps: {
    dragId: string | null;
    onDragStart: (id: string) => void;
    onDragEnd: () => void;
  };
}

/**
 * En kolumn (aktivt steg) eller mini-zon (terminal). Samma chassi: 3px toppband
 * i statusfärg (getStatusVariantKey — SAMMA SSOT som rail/status-taggar, ingen
 * drift), mono-namn + antal-chip, kap 4 kort + "Visa N fler", drop-target-
 * highlight via `data-over`. `variant="zone"` ger kompakta kort.
 */
function BoardColumn({
  status,
  label,
  variant,
  apps,
  now,
  isOver,
  emptyText,
  dropProps,
  cardProps,
}: BoardColumnProps) {
  const tUi = useTranslations("applications.ui");
  const [expanded, setExpanded] = useState(false);

  const overCap = apps.length > COLUMN_CARD_CAP;
  const visible = expanded ? apps : apps.slice(0, COLUMN_CARD_CAP);
  const hidden = apps.length - visible.length;

  return (
    <section
      className="jp-board-col"
      // role="group" (ej implicit region-landmark): 10 kolumner/zoner skulle
      // annars bli 10 landmarks = brus i skärmläsarens landmark-meny. aria-label
      // (fullt stegnamn) bevaras, så AT täcker de trunkerade huvudena (a11y-skill).
      role="group"
      data-variant={variant}
      data-over={isOver || undefined}
      // 3px toppband i statusfärg (design §6) via border-top, SAMMA
      // data-status-variant-mekanik + status-tokens som stegrailen (DRY, ingen
      // drift). getStatusVariantKey → info/brand/success/warning/danger/neutral.
      data-status-variant={getStatusVariantKey(status)}
      aria-label={label}
      onDragOver={dropProps.onDragOver}
      onDragLeave={dropProps.onDragLeave}
      onDrop={dropProps.onDrop}
    >
      <div className="jp-board-col__head">
        {/* De smala kolumnerna kan trunkera de langa stegnamnen ("Intervju
            bokad"); title ger full text vid hover (sektionens aria-label bar
            redan hela namnet for skarmlasare). */}
        <span className="jp-board-col__name jp-mono" title={label}>
          {label}
        </span>
        {/* #805 punkt 2: antalet inline "(N)" intill kolumnnamnet (var tidigare
            en chip-pill) → samma form som Lista/Alla-vyerna (3-vy-konsekvens). */}
        <span className="jp-board-col__count">({apps.length})</span>
      </div>
      <div className="jp-board-col__list">
        {apps.length === 0 ? (
          <p className="jp-board-col__empty">{emptyText}</p>
        ) : (
          <>
            {visible.map((application) => (
              <ApplicationBoardCard
                key={application.id}
                application={application}
                now={now}
                compact={variant === "zone"}
                isDragging={cardProps.dragId === application.id}
                onDragStart={() => cardProps.onDragStart(application.id)}
                onDragEnd={cardProps.onDragEnd}
              />
            ))}
            {overCap && (
              <button
                type="button"
                className="jp-board-col__more"
                onClick={() => setExpanded((value) => !value)}
              >
                {expanded
                  ? tUi("board.showFewer")
                  : tUi("board.showMore", { count: hidden })}
              </button>
            )}
          </>
        )}
      </div>
    </section>
  );
}
