"use client";

import { useId, useState, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import { getStatusLabel } from "@/lib/applications/status";
import type {
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/types/applications";

// Fast pipeline-ordning (speglar PIPELINE_ORDER i page.tsx). Översiktsraden
// visar alla 10 statusar i denna ordning; sektionerna nedan filtreras till
// count > 0 men behåller samma ordning.
const PIPELINE_ORDER: ApplicationStatus[] = [
  "Draft",
  "Submitted",
  "Acknowledged",
  "InterviewScheduled",
  "Interviewing",
  "OfferReceived",
  "Accepted",
  "Rejected",
  "Withdrawn",
  "Ghosted",
];

interface ApplicationsPipelineProps {
  // Serialiserad pipeline-data från RSC (page.tsx). Alla 10 grupper, även
  // tomma — översiktsraden behöver dem; sektionsvyn filtrerar count > 0.
  groups: PipelineGroupDto[];
  // ApplicationRow förblir server-renderbar (CTO punkt 4). page.tsx (RSC)
  // server-renderar raderna och passar in dem som en ReactNode[]-slot-map
  // keyad på status. Renderad ReactNode är serialiserbar över RSC→Client-
  // gränsen — en render-prop-FUNKTION är det INTE (Next.js use-client.md
  // rad 50-57). Client-ön slår upp slots per group.status; den anropar
  // ingen funktion och äger aldrig rad-utseendet.
  rowSlots: Record<ApplicationStatus, ReactNode[]>;
}

interface PipelineSectionProps {
  group: PipelineGroupDto;
  rows: ReactNode[];
}

// En statusgrupp som ledger-sektion med opt-in minimera-disclosure.
// Civic-disclosure-mönstret är taget verbatim från job-ad-filters.tsx
// (button aria-expanded/aria-controls + ChevronDown-rotation, open-default).
function PipelineSection({ group, rows }: PipelineSectionProps) {
  // Alla grupper expanderade vid sidladdning. Minimera = opt-in per grupp.
  // Ingen persistens, inget auto-kollaps, inget tröskelvärde.
  const [open, setOpen] = useState(true);
  const panelId = useId();
  const label = getStatusLabel(group.status);

  return (
    <section id={`status-${group.status}`} aria-label={label} className="scroll-mt-6">
      <div className="mb-2 flex items-baseline gap-3 border-b border-border-strong pb-2">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          aria-controls={panelId}
          className="flex items-center gap-2 text-text-primary"
        >
          <ChevronDown
            className={`size-4 transition-transform duration-150 ${
              open ? "" : "-rotate-90"
            }`}
            aria-hidden="true"
          />
          <span className="jp-h2">{label}</span>
          <span className="sr-only">
            {open
              ? `, minimera gruppen ${label}`
              : `, expandera gruppen ${label}`}
          </span>
        </button>
        <span className="font-mono text-[13px] font-medium text-text-secondary">
          {group.count}
        </span>
      </div>
      {open && (
        <div
          id={panelId}
          className="flex flex-col border-t border-border-default"
        >
          {rows}
        </div>
      )}
    </section>
  );
}

/**
 * Client-ö för /ansokningar-listan (CTO-beslut a316ed539d85b2f79, superseder
 * a8e269eb-transportmekanismen). Arkitekturen är oförändrad: page.tsx förblir
 * RSC + äger auth/error/total===0; ApplicationRow förblir server-renderbar.
 *
 * Transportmekanismen är fixad: ApplicationRow-elementen server-renderas i
 * page.tsx och passas in som en serialiserbar ReactNode[]-slot-map (rowSlots)
 * keyad på status — INGEN render-prop-funktion (Next.js use-client.md rad
 * 50-57: "Function is not serializable" över RSC→Client-gränsen, vilket
 * orsakade prod-incidenten / commit eece124).
 *
 * Client-ön äger tre interaktioner:
 *
 *  1. Översiktsrad — ren in-page-ankarnav över alla 10 statusar i fast
 *     ordning. 0-count = inert dämpat span; icke-tom = ankarlänk till
 *     #status-<Status>. Ingen filtrering, ingen scroll-logik (browserns
 *     native fragment-navigation sköter scroll).
 *  2. Per-grupp kollaps-state (useState, alla expanderade default).
 *  3. Disclosure-toggles per grupprubrik (opt-in minimera).
 */
export function ApplicationsPipeline({
  groups,
  rowSlots,
}: ApplicationsPipelineProps) {
  // Översiktsraden: alla 10 i fast ordning oavsett count.
  const byStatus = new Map(groups.map((g) => [g.status, g]));
  const overview = PIPELINE_ORDER.map((status) => {
    const group = byStatus.get(status);
    return { status, count: group?.count ?? 0 };
  });

  // Sektioner: bara count > 0, behåll pipeline-ordning.
  const sections = PIPELINE_ORDER.map((status) => byStatus.get(status)).filter(
    (g): g is PipelineGroupDto => g != null && g.count > 0
  );

  return (
    <div className="mt-8 flex flex-col gap-8">
      <nav
        aria-label="Statusöversikt"
        className="flex flex-wrap gap-x-5 gap-y-2 border-y border-border-default py-3"
      >
        {overview.map(({ status, count }) => {
          const label = getStatusLabel(status);
          const countNode = (
            <span className="font-mono text-[13px] font-medium tabular-nums">
              {count}
            </span>
          );

          if (count === 0) {
            // Inert: tomt = inget att navigera till. Ej fokuserbar länk.
            return (
              <span
                key={status}
                data-testid={`overview-item-${status}`}
                data-overview-item=""
                data-status={status}
                className="flex items-baseline gap-1.5 text-body-sm text-text-secondary"
              >
                <span>{label}</span>
                {countNode}
              </span>
            );
          }

          return (
            <a
              key={status}
              href={`#status-${status}`}
              data-testid={`overview-item-${status}`}
              data-overview-item=""
              data-status={status}
              className="flex items-baseline gap-1.5 text-body-sm text-text-primary underline-offset-2 hover:underline"
            >
              <span>{label}</span>
              {countNode}
            </a>
          );
        })}
      </nav>

      <div className="flex flex-col gap-8">
        {sections.map((group) => (
          <PipelineSection
            key={group.status}
            group={group}
            rows={rowSlots[group.status] ?? []}
          />
        ))}
      </div>
    </div>
  );
}
