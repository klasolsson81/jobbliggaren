"use client";

import { useMemo } from "react";
import { useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  getStatusVariantKey,
  isActivePipelineStatus,
  PIPELINE_ORDER,
} from "@/lib/applications/status";
import type { ApplicationStatus, PipelineGroupDto } from "@/lib/dto/applications";

interface StepRailProps {
  groups: PipelineGroupDto[];
  statusFilter: ApplicationStatus | null;
  onToggle: (status: ApplicationStatus) => void;
}

/**
 * Stegrail "ALLA 10 STEG" (design 2a §7) — helhetsbilden av pipelinen som ALLTID
 * visar alla 10 steg (tomma dimmade, opacity .55, aldrig borttagna) så
 * översikten inte försvinner när en status saknar ansökningar. Varje cell är en
 * riktig `<button aria-pressed>` (a11y — filter är en toggle, inte en länk):
 * klick sätter/rensar stegfiltret på Lista-vyn. Cellens 3px-toppkant färgkodas i
 * stegets statusfärg via `data-status-variant` (samma befintliga status-tokens
 * som status-taggen; grå `--jp-border` vid antal 0). Terminala/vilande steg får
 * `--jp-surface-2`; skiljelinje `--jp-border-strong` före Accepterad.
 *
 * Antalen läses direkt ur `groups[].count` (backend-sanning, redan på plats) —
 * ingen ny hämtning. Railen gäller Lista-vyn; i Tavla-vyn (PR 8) döljs den.
 */
export function StepRail({ groups, statusFilter, onToggle }: StepRailProps) {
  const tEnum = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");

  const countByStatus = useMemo(
    () => new Map(groups.map((g) => [g.status, g.count])),
    [groups],
  );

  return (
    <div className="jp-steprail">
      <div className="jp-steprail__labelrow">
        <span className="jp-steprail__kicker jp-mono">{tUi("rail.kicker")}</span>
        <span className="jp-steprail__hint">{tUi("rail.hint")}</span>
      </div>
      <div
        className="jp-steprail__cells"
        role="group"
        aria-label={tUi("rail.ariaLabel")}
      >
        {PIPELINE_ORDER.map((status, index) => {
          const count = countByStatus.get(status) ?? 0;
          const selected = statusFilter === status;
          const active = isActivePipelineStatus(status);
          // Skiljelinjen ligger före den FÖRSTA terminala cellen (Accepterad):
          // den föregås av en aktiv cell i pipelineordningen.
          const prev = index > 0 ? PIPELINE_ORDER[index - 1] : undefined;
          const divider = !active && prev != null && isActivePipelineStatus(prev);
          const label = applicationStatusLabel(tEnum, status);
          // Tomt steg (antal 0) = inget att filtrera till → cellen är `disabled`
          // (design-reviewer Blocker): det tar bort en död-ände-affordans OCH gör
          // 0.55-dimningen WCAG-compliant (disabled-kontroller är kontrast-
          // undantagna, båda teman). Cellen ligger kvar i a11y-trädet så
          // skärmläsaren ändå läser "Bekräftad, inga ansökningar".
          const empty = count === 0;
          return (
            <button
              key={status}
              type="button"
              className="jp-steprail__cell"
              data-status-variant={getStatusVariantKey(status)}
              data-empty={empty}
              data-terminal={!active}
              data-divider={divider}
              disabled={empty}
              aria-pressed={selected}
              aria-label={tUi("rail.cellAriaLabel", { count, step: label })}
              onClick={() => onToggle(status)}
            >
              <span className="jp-steprail__count">{count}</span>
              <span className="jp-steprail__name jp-mono">{label}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}
