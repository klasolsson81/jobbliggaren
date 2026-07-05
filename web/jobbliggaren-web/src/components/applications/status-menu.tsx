"use client";

import { useTranslations } from "next-intl";
import { Check, ChevronDown } from "lucide-react";
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
 */
export function StatusMenu({ application }: { application: ApplicationDto }) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const { pendingId, transition } = useApplicationActions();
  const pending = pendingId === application.id;

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
        <button
          type="button"
          className="jp-rowbtn jp-rowbtn--ink"
          disabled={pending}
        >
          {tUi("statusMenu.trigger")}
          <ChevronDown size={14} aria-hidden="true" />
        </button>
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
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
