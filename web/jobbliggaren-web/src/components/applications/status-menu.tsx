"use client";

import { useTranslations } from "next-intl";
import { Check, ChevronDown, Trash2 } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  ACTIVE_PIPELINE_STATUSES,
  applicationStatusLabel,
  getStatusVariantKey,
  STATUS_MENU_CLOSED_GROUP,
} from "@/lib/applications/status";
import { useApplicationActions } from "./application-actions";
import type { ApplicationDto, ApplicationStatus } from "@/lib/dto/applications";

/**
 * "Byt status ▾"-menyn (#630 PR 7, design §5): 250px popover med grupperna
 * "FLYTTA TILL · AKTIV VÄG" (6 steg) och "AVSLUT & VILANDE" (Accepterad/Nekad/
 * Återtagen + Ghosted). Färgprick (2px-radie kvadrat i stegets statusfärg,
 * samma status-tokens som taggen — WCAG 1.4.1: färgen FÖRSTÄRKER etiketten,
 * bär den aldrig) + namn + ✓ på nuvarande. Alla 10 val alltid tillgängliga —
 * fria hopp åt båda håll (ADR 0092 D3); nuvarande status är disabled (ett byte
 * till samma status vore en tyst no-op).
 *
 * Byggd på ui/dropdown-menu (Radix, CTO-bind 5) — APG-menyknapp-kontraktet
 * (roving focus, typeahead, Escape i capture-fas som #565-skalen yield:ar till).
 *
 * `compact` (#630 PR 10, design §7): Tabell-vyns status-cell använder den lilla
 * ▾-only-triggern (22×22, synlig kant) i stället för fulltext-"Byt status" —
 * etiketten bärs av aria-label. Samma meny, samma a11y-kontrakt.
 */
export function StatusMenu({
  application,
  pending,
  compact = false,
}: {
  application: ApplicationDto;
  /**
   * Pågår ett statusbyte på DENNA rad (disable:ar menyvalen + triggern)? Trådas
   * ned från vy-containern (perf-audit d4) — menyn prenumererar aldrig själv på
   * pendingIds-Set:et, så den re-renderar inte vid andra raders byten.
   */
  pending: boolean;
  compact?: boolean;
}) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const { transition, deleteApplication } = useApplicationActions();

  const renderItem = (status: ApplicationStatus) => {
    const current = status === application.status;
    return (
      <DropdownMenuItem
        key={status}
        className="jp-statusmenu__item"
        disabled={current || pending}
        onSelect={() => transition(application, status)}
      >
        <span
          className="jp-statusmenu__dot"
          data-status-variant={getStatusVariantKey(status)}
          aria-hidden="true"
        />
        <span className="min-w-0 flex-1 truncate">
          {applicationStatusLabel(t, status)}
        </span>
        {current && (
          <>
            <Check size={16} aria-hidden="true" />
            <span className="sr-only">{tUi("statusMenu.currentSuffix")}</span>
          </>
        )}
      </DropdownMenuItem>
    );
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        {compact ? (
          <button
            type="button"
            className="jp-statusmenu__minitrigger"
            disabled={pending}
            aria-label={tUi("statusMenu.trigger")}
          >
            <ChevronDown size={14} aria-hidden="true" />
          </button>
        ) : (
          <button
            type="button"
            className="jp-rowbtn jp-rowbtn--ink"
            disabled={pending}
          >
            {tUi("statusMenu.trigger")}
            <ChevronDown size={14} aria-hidden="true" />
          </button>
        )}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="jp-statusmenu">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="jp-statusmenu__label jp-mono">
            {tUi("statusMenu.groupActive")}
          </DropdownMenuLabel>
          {ACTIVE_PIPELINE_STATUSES.map(renderItem)}
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuGroup>
          <DropdownMenuLabel className="jp-statusmenu__label jp-mono">
            {tUi("statusMenu.groupClosed")}
          </DropdownMenuLabel>
          {STATUS_MENU_CLOSED_GROUP.map(renderItem)}
        </DropdownMenuGroup>
        {/* #782 (ADR 0104) — destructive HARD delete. Placed behind the overflow
            menu with a mandatory confirm dialog (deleteApplication opens the ONE
            shared DeleteApplicationDialog on the island) rather than a bare card
            red-×, so a whole-record delete cannot be fat-fingered. Distinct intent
            from the Withdrawn status above: återta = keep the record; ta bort =
            remove it. Separated + danger-tinted (colour reinforces, the label
            carries the meaning — WCAG 1.4.1). */}
        <DropdownMenuSeparator />
        <DropdownMenuItem
          // DropdownMenuItem-primitiven sätter ovillkorligt focus:text-accent-
          // foreground (0-2-0) som annars slår .text-danger-700 (0-1-0) → posten
          // tappar sin röda identitet exakt i interaktionsögonblicket (Radix
          // flyttar fokus vid hover också). Bevara danger genom focus/hover, per
          // repo-precedens ui/select.tsx (design-reviewer Major 1, #782).
          className="jp-statusmenu__item text-danger-700 focus:bg-danger-50 focus:text-danger-700"
          disabled={pending}
          onSelect={() => deleteApplication(application)}
        >
          <Trash2 size={16} aria-hidden="true" />
          <span className="min-w-0 flex-1 truncate">
            {tUi("statusMenu.delete")}
          </span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
