"use client";

import { useMemo, useRef, useState, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import { ChevronDown } from "lucide-react";
import {
  applicationStatusLabel,
  ATTENTION_SIGNAL_BUCKET,
  ATTENTION_SIGNAL_ORDER,
  attentionReasonKey,
  isFiringSignal,
  PIPELINE_ORDER,
} from "@/lib/applications/status";
import type {
  ApplicationAttentionSignal,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

type FiringSignal = Exclude<ApplicationAttentionSignal, "None">;

interface ApplicationsPipelineProps {
  // Serialiserad pipeline-data från RSC (page.tsx). Alla 10 grupper, även
  // tomma. groups[status].applications[i] är POSITIONELLT alignad med
  // rowSlots[status][i] OCH bär nu .attentionSignal (#343, ADR 0085) — så
  // client-ön kan bygga både feeden och statussektionerna PURT från groups +
  // rowSlots utan en ny datahämtning över RSC→Client-gränsen.
  groups: PipelineGroupDto[];
  // ApplicationRow förblir server-renderbar (F3/CTO-mönster). page.tsx (RSC)
  // server-renderar raderna och passar in dem som en ReactNode[]-slot-map
  // keyad på status. Renderad ReactNode är serialiserbar över RSC→Client-
  // gränsen — en render-prop-FUNKTION är det INTE (Next.js use-client.md
  // rad 50-57; orsakade prod-incidenten i commit eece124). Client-ön slår
  // upp slots per group.status; den anropar ingen funktion och äger aldrig
  // rad-utseendet. Feeden VÄLJER befintliga rad-noder — den skapar inga nya.
  rowSlots: Record<ApplicationStatus, ReactNode[]>;
}

// Statussektioner som är öppna vid sidladdning (aktiva tillstånd). Terminala
// tillstånd (Rejected/Withdrawn/Ghosted/Accepted) är default kollapsade —
// kollaps är den nya navigerings-/skalningsmekanismen (RULING 2/3).
const DEFAULT_OPEN_STATUSES: ReadonlySet<ApplicationStatus> = new Set<
  ApplicationStatus
>([
  "Draft",
  "Submitted",
  "Acknowledged",
  "InterviewScheduled",
  "Interviewing",
  "OfferReceived",
]);

// Synliga rader per ÖPPEN statussektion innan "Visa fler" (RULING 4). Enkel
// konstant, ingen config. Gäller INTE attention-feeden (allt som kräver åtgärd
// visas alltid).
const SECTION_ROW_CAP = 10;

interface AttentionItem {
  key: string;
  signal: FiringSignal;
  node: ReactNode;
}

/**
 * Client-ö för /ansokningar-listan (#343 prio-lista, ADR 0085). page.tsx
 * förblir RSC + äger auth/error/total===0; ApplicationRow förblir
 * server-renderbar och passas som serialiserbar ReactNode[]-slot-map
 * (rowSlots) keyad på status — INGEN render-prop-funktion över
 * RSC→Client-gränsen, INGEN ny datahämtning.
 *
 * Prioriteringen (design-reviewer-bind, RULING 1–5):
 *  - MOVE: varje ansökan renderas EXAKT EN gång. Har den en attention-signal
 *    (!== "None") lyfts den till den pinnade "Kräver åtgärd"-feeden och tas
 *    BORT ur sin statussektion. Feeden bygger INGA nya rad-noder — den väljer
 *    befintliga rowSlots-noder, lindade med sin orsaksrad.
 *  - Statussektioner: rad-slots filtrerade till index där signalen är
 *    "None"/undefined; head visar "visar {shown} av {total}" där total =
 *    backend-count (sanningsenlig) och shown = synliga (ej lyfta) rader. En
 *    sektion vars rader ALLA lyfts (0 synliga) renderas inte.
 *  - Kollaps: varje statussektions-head är en knapp (aria-expanded +
 *    aria-controls). Feeden är ALLTID öppen, pinnad överst, aldrig kollapsbar.
 *  - "Visa fler": cap 10 synliga rader per öppen sektion, sedan en knapp som
 *    expanderar hela sektionen.
 */
export function ApplicationsPipeline({
  groups,
  rowSlots,
}: ApplicationsPipelineProps) {
  const tEnum = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const tAttention = useTranslations("applications.ui.attention");

  const byStatus = useMemo(
    () => new Map(groups.map((g) => [g.status, g])),
    [groups]
  );

  // Attention-feeden: för varje grupp, för varje radindex i där
  // applications[i] har en fyrande signal, välj rowSlots[status][i] (befintlig
  // nod) och linda den med sin orsak. Sortera feeden på signalprioritet
  // (offer → overdue → draft-deadline → no-response → nudge). Feeden capas
  // ALDRIG — allt som kräver åtgärd visas.
  const attentionItems = useMemo<AttentionItem[]>(() => {
    const items: AttentionItem[] = [];
    for (const status of PIPELINE_ORDER) {
      const group = byStatus.get(status);
      if (group == null) continue;
      const slots = rowSlots[status] ?? [];
      group.applications.forEach((application, index) => {
        const signal = application.attentionSignal;
        if (!isFiringSignal(signal)) return;
        const node = slots[index];
        if (node == null) return; // defensivt: slot/app misalign (deploy-skew)
        items.push({ key: `${status}-${index}`, signal, node });
      });
    }
    const rank = new Map(
      ATTENTION_SIGNAL_ORDER.map((s, i) => [s, i] as const)
    );
    return items.sort(
      (a, b) =>
        (rank.get(a.signal) ?? Number.MAX_SAFE_INTEGER) -
        (rank.get(b.signal) ?? Number.MAX_SAFE_INTEGER)
    );
  }, [byStatus, rowSlots]);

  // Statussektioner: rowSlots filtrerade till index där signalen INTE fyrar
  // (None/undefined) — de lyfta raderna är redan i feeden (MOVE, ingen
  // dubbelrendering). En sektion vars rader alla lyfts (0 synliga) renderas
  // inte; group.count förblir backend-totalen (sanningsenlig).
  const sections = useMemo(() => {
    return PIPELINE_ORDER.map((status) => {
      const group = byStatus.get(status);
      if (group == null || group.count === 0) return null;
      const slots = rowSlots[status] ?? [];
      const visibleNodes: ReactNode[] = [];
      group.applications.forEach((application, index) => {
        if (isFiringSignal(application.attentionSignal)) return; // lyft → feed
        const node = slots[index];
        if (node != null) visibleNodes.push(node);
      });
      if (visibleNodes.length === 0) return null; // helt dränerad → ingen rubrik
      return { status, total: group.count, visibleNodes };
    }).filter((s): s is NonNullable<typeof s> => s != null);
  }, [byStatus, rowSlots]);

  if (attentionItems.length === 0 && sections.length === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">{tUi("pipeline.emptyTitle")}</div>
        {tUi("pipeline.emptyBody")}
      </div>
    );
  }

  return (
    <>
      {/* "Kräver åtgärd"-feeden — pinnad överst, ALLTID öppen, aldrig
          kollapsbar. Tom feed → render NOTHING (ingen tom rubrik). DOM-ordning
          = visuell ordning (feed först). */}
      {attentionItems.length > 0 && (
        <section
          className="jp-attention"
          aria-labelledby="attention-heading"
        >
          <h2 id="attention-heading" className="jp-attention__title">
            {tUi("pipeline.attentionTitle")}
          </h2>
          <div className="jp-applist">
            {attentionItems.map((item) => (
              <div key={item.key} className="jp-attention__item">
                <p
                  className="jp-attention__reason"
                  data-signal={ATTENTION_SIGNAL_BUCKET[item.signal]}
                >
                  <span className="jp-attention__dot" aria-hidden="true" />
                  <span className="jp-attention__text">
                    {tAttention(attentionReasonKey(item.signal))}
                  </span>
                </p>
                {item.node}
              </div>
            ))}
          </div>
        </section>
      )}

      {sections.map((section) => (
        <StatusSection
          key={section.status}
          status={section.status}
          total={section.total}
          visibleNodes={section.visibleNodes}
          defaultOpen={DEFAULT_OPEN_STATUSES.has(section.status)}
          label={applicationStatusLabel(tEnum, section.status)}
        />
      ))}
    </>
  );
}

interface StatusSectionProps {
  status: ApplicationStatus;
  total: number;
  visibleNodes: ReactNode[];
  defaultOpen: boolean;
  label: string;
}

/**
 * En statussektion. WAI accordion-mönster: en <h2> WRAPPAR en knapp
 * (aria-expanded + aria-controls) — ingen aria-label, så knappens accessible
 * name = den synliga texten (label + "visar X av Y") och rubriken navigeras som
 * heading. Chevron är aria-hidden. Default öppen för aktiva tillstånd, kollapsad
 * för terminala. Innehåller "Visa fler"-cap (10 rader) per öppen sektion. Hämtar
 * sin egen `useTranslations` (samma client-ö) i stället för en passad translator
 * — bevarar den namespace-precisa typningen (annars TS2589 "excessively deep")
 * och håller komponenten självförsörjande.
 */
function StatusSection({
  status,
  total,
  visibleNodes,
  defaultOpen,
  label,
}: StatusSectionProps) {
  const tUi = useTranslations("applications.ui");
  const [open, setOpen] = useState(defaultOpen);
  const [expanded, setExpanded] = useState(false);
  const headRef = useRef<HTMLButtonElement>(null);

  const listId = `status-${status}-list`;
  const shown = visibleNodes.length;
  const overCap = shown > SECTION_ROW_CAP;
  const rows = expanded ? visibleNodes : visibleNodes.slice(0, SECTION_ROW_CAP);
  const hiddenCount = shown - rows.length;

  // "Visa fler" → expandera + flytta fokus till sektions-headen, eftersom
  // knappen försvinner när alla rader visas (annars tappas fokus). RULING 4.
  const onShowMore = () => {
    setExpanded(true);
    headRef.current?.focus();
  };

  return (
    <section
      id={`status-${status}`}
      aria-label={label}
      className="jp-section scroll-mt-6"
    >
      {/* WAI accordion-mönster: rubriken WRAPPAR knappen (ingen aria-label —
          den skulle överrida subträdet och dölja "visar X av Y"). Knappens
          accessible name kommer från den synliga texten ({label} visar X av Y);
          aria-expanded bär öppen/stängd. h2 navigeras som rubrik. */}
      <h2 className="jp-section__heading">
        <button
          ref={headRef}
          type="button"
          className="jp-section__head jp-section__toggle"
          aria-expanded={open}
          aria-controls={listId}
          onClick={() => setOpen((v) => !v)}
        >
          <ChevronDown
            size={18}
            className="jp-section__chevron"
            data-open={open}
            aria-hidden="true"
          />
          <span className="jp-section__title-text">{label}</span>
          <span className="jp-section__count">
            {tUi("pipeline.sectionShownOfTotal", { shown, total })}
          </span>
        </button>
      </h2>

      {open && (
        <div id={listId}>
          <div className="jp-applist">{rows}</div>
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
