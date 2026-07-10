"use client";

import type { MouseEvent } from "react";
import { useTranslations } from "next-intl";
import { applicationStatusLabel, nextStepOf } from "@/lib/applications/status";
import { useApplicationActions } from "./application-actions";
import type { RowAction } from "./application-row";
import type {
  ApplicationDto,
  ApplicationStatus,
} from "@/lib/dto/applications";

// clientY-fallback för dialog-/panel-ankaret: ett programmatiskt klick (utan
// verklig pekare) faller tillbaka på knappens position, aldrig 0 (design §9
// positionering, #630 PR 6/7).
function anchorY(event: MouseEvent<HTMLButtonElement>): number {
  return event.clientY > 0
    ? event.clientY
    : event.currentTarget.getBoundingClientRect().top;
}

/**
 * Radens default-primär-CTA, berikad med sin SORT så konsumenter kan välja
 * kontext-label: Lista-raden visar `label` rakt av ("Flytta till {nästa}"),
 * Tabell-vyns "Nästa steg"-kolumn renderar moveToNext-fallet som kompakt
 * "→ {nästa}" (design §7) men ärver Draft/Ghosted-specialen som de är.
 */
export type DefaultRowAction = RowAction & {
  kind: "finishDraft" | "reactivate" | "moveToNext";
  /** Satt för kind="moveToNext" — målsteget (för pil-labeln "→ {status}"). */
  nextStatus?: ApplicationStatus;
};

/**
 * Delad SSOT för radens DEFAULT-primär-CTA (design §5, prototyp-facit): utkast →
 * "Slutför och skicka"-dialogen (mellansteg §9); Ghosted → "Återaktivera"
 * (→ Skickad); annars "Flytta till {nästa}"; terminala → ingen. Extraherad ur
 * `ApplicationRow` (#630 PR 7) så Lista-raden OCH Tabell-vyns "Nästa steg"-kolumn
 * (#630 PR 10) delar EN härledning (CLAUDE.md §9.1 DRY, senior-cto-advisor Fork
 * 4). Ren presentations-mappning (vilken knapp/länk visas) — ALDRIG en
 * transitions-grind; backend tillåter alla byten (ADR 0092 D3).
 *
 * OBS: kökortets signal→CTA-karta (`attention-queue.tsx` `cardActions`) är ETT
 * ANNAT kunskapsstycke (signal-nyckel, urgens-driven) och foldas medvetet INTE in
 * här — olika förändringsskäl (SRP/CCP, senior-cto-advisor Fork 4).
 */
export function useRowActions() {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const { transition, openFinishDraft } = useApplicationActions();

  const defaultPrimaryFor = (
    application: ApplicationDto,
  ): DefaultRowAction | null => {
    const { status } = application;
    const next = nextStepOf(status);

    if (status === "Draft") {
      return {
        kind: "finishDraft",
        label: tUi("row.finishAndSend"),
        onClick: (event) => openFinishDraft(application, anchorY(event)),
      };
    }
    if (status === "Ghosted") {
      return {
        kind: "reactivate",
        label: tUi("row.reactivate"),
        onClick: () => transition(application, "Submitted"),
      };
    }
    if (next != null) {
      return {
        kind: "moveToNext",
        nextStatus: next,
        label: tUi("row.moveToNext", {
          status: applicationStatusLabel(t, next),
        }),
        onClick: () => transition(application, next),
      };
    }
    return null;
  };

  return { defaultPrimaryFor };
}
