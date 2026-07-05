"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronDown } from "lucide-react";
import type { ApplicationDto, ApplicationStatus } from "@/lib/dto/applications";
import { ApplicationRow } from "./application-row";

// Synliga rader per ÖPPEN statussektion innan "Visa fler" (design 2a §5). Enkel
// konstant, ingen config.
const SECTION_ROW_CAP = 10;

interface StatusSectionProps {
  status: ApplicationStatus;
  label: string;
  // Ansökningar i gruppen som matchar aktuellt sök (containern har redan
  // filtrerat). Renderas som rader; kapas till SECTION_ROW_CAP tills expanderad.
  applications: ApplicationDto[];
  now: Date;
  // Öppen vid sidladdning (design §5: Skickad/Intervju bokad/Erbjudande).
  defaultOpen: boolean;
  // Aktivt filter/sökning tvingar träffgruppen öppen (design §5) så resultatet
  // aldrig göms bakom en kollapsad rubrik.
  forceOpen: boolean;
}

/**
 * En statussektion i Lista-vyn (design 2a §5). WAI-accordion: en <h2> WRAPPAR en
 * knapp (aria-expanded + aria-controls) — ingen aria-label, så knappens
 * accessible name = den synliga texten (label + antal) och rubriken navigeras som
 * heading. Chevron aria-hidden. Default öppen för Skickad/Intervju bokad/
 * Erbjudande, annars kollapsad — kollaps är skalningsmekanismen. Vid aktivt
 * filter/sök tvingas sektionen öppen (`forceOpen`). Renderar raderna direkt ur
 * DTO:n (data-pivoten, ADR 0092 D2 — inga ReactNode-slots längre). Hämtar sin
 * egen `useTranslations` (namespace-precis typning; annars TS2589).
 */
export function StatusSection({
  status,
  label,
  applications,
  now,
  defaultOpen,
  forceOpen,
}: StatusSectionProps) {
  const tUi = useTranslations("applications.ui");
  const [openState, setOpenState] = useState(defaultOpen);
  const [expanded, setExpanded] = useState(false);
  const headRef = useRef<HTMLButtonElement>(null);

  // forceOpen vinner: en träffgrupp under aktivt filter/sök är alltid öppen.
  // Användarens egen toggle-preferens (openState) bevaras och återtar effekt när
  // filtret rensas.
  const open = forceOpen || openState;

  const listId = `status-${status}-list`;
  const shown = applications.length;
  const overCap = shown > SECTION_ROW_CAP;
  const rows = expanded ? applications : applications.slice(0, SECTION_ROW_CAP);
  const hiddenCount = shown - rows.length;

  // "Visa fler" → expandera + flytta fokus till sektions-headen, eftersom knappen
  // försvinner när alla rader visas (annars tappas fokus). Design §5.
  const onShowMore = () => {
    setExpanded(true);
    headRef.current?.focus();
  };

  return (
    <section
      id={`status-${status}`}
      aria-label={label}
      className="jp-section jp-section--group scroll-mt-6"
    >
      <h2 className="jp-section__heading">
        <button
          ref={headRef}
          type="button"
          className="jp-section__head jp-section__toggle jp-section__toggle--group"
          aria-expanded={open}
          aria-controls={listId}
          // Under forceOpen (aktivt filter/sök) är sektionen låst öppen; klicket
          // muterar då INTE openState (annars läcker ett dolt kollapsat läge fram
          // när filtret rensas — code-review Nit). Utan forceOpen togglar det som
          // vanligt.
          onClick={() => {
            if (!forceOpen) setOpenState((v) => !v);
          }}
        >
          <ChevronDown
            size={18}
            className="jp-section__chevron"
            data-open={open}
            aria-hidden="true"
          />
          <span className="jp-section__title-text">{label}</span>
          <span className="jp-section__count">{shown}</span>
          {!open && (
            <span className="jp-section__closedhint">
              {tUi("pipeline.clickToShow")}
            </span>
          )}
        </button>
      </h2>

      {open && (
        <div id={listId}>
          <div className="jp-applist">
            {rows.map((application) => (
              <ApplicationRow
                key={application.id}
                application={application}
                now={now}
              />
            ))}
          </div>
          {overCap && !expanded && (
            <button
              type="button"
              className="jp-btn jp-btn--secondary jp-section__more"
              onClick={onShowMore}
            >
              {tUi("pipeline.showMore", { count: hiddenCount })}
            </button>
          )}
        </div>
      )}
    </section>
  );
}
