"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronDown } from "lucide-react";
import type { ApplicationDto, ApplicationStatus } from "@/lib/dto/applications";
import { useApplicationPending } from "./application-actions";
import { ApplicationRow } from "./application-row";
import type { InformationSnapshots } from "@/components/information/types";
import { useInformationAdapter, useInformationSnapshot } from "@/components/information/useInformationAdapter";

// Synliga rader per ÖPPEN statussektion innan "Visa N till" (design 2a §5). Enkel
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
  const restored = useInformationSnapshot(`status:${status}`);
  // Läser pendingIds-Set:et och trådar ett per-rad `pending`-prop ned (d4). Denna
  // sektion re-renderar vid ett statusbyte (billig map), men de memo-lindade
  // raderna skippar utom den vars `pending` faktiskt flippade.
  const pendingIds = useApplicationPending();
  const [openState, setOpenState] = useState(restored?.open ?? defaultOpen);
  const [expanded, setExpanded] = useState(restored?.expanded ?? false);
  const headRef = useRef<HTMLButtonElement>(null);
  useInformationAdapter(`status:${status}`, (): InformationSnapshots[`status:${ApplicationStatus}`] => ({ open: openState, expanded }));

  // forceOpen vinner: en träffgrupp under aktivt filter/sök är alltid öppen.
  // Användarens egen toggle-preferens (openState) bevaras och återtar effekt när
  // filtret rensas.
  const open = forceOpen || openState;

  const listId = `status-${status}-list`;
  const shown = applications.length;
  const overCap = shown > SECTION_ROW_CAP;
  const rows = expanded ? applications : applications.slice(0, SECTION_ROW_CAP);
  const hiddenCount = shown - rows.length;

  // "Visa N till" → expandera + flytta fokus till sektions-headen, eftersom knappen
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
      {/* h3: statusgrupperna är subsektioner under "Alla ansökningar" (h2) —
          korrekt rubrikutline (design-reviewer Minor 3). */}
      <h3 className="jp-section__heading">
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
          {/* #805 punkt 2: antalet inline "Skickad 1" intill etiketten (var
              tidigare långt till höger via margin-left:auto), utan parentes
              (#1827). Samma form som "Alla ansökningar" och Tavla-kolumnerna →
              de tre vyerna läser konsistent. Mellanslaget hålls i knappens namn
              ("Skickad 1"); flex-layouten ritar det inte. */}{" "}
          <span className="jp-section__count">{shown}</span>
        </button>
      </h3>

      {open && (
        <div id={listId}>
          <div className="jp-applist">
            {rows.map((application) => (
              <ApplicationRow
                key={application.id}
                application={application}
                now={now}
                pending={pendingIds.has(application.id)}
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
