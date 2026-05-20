"use client";

import { useMemo, useState, type ReactNode } from "react";
import { getStatusLabel, PIPELINE_ORDER } from "@/lib/applications/status";
import type {
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/types/applications";

interface ApplicationsPipelineProps {
  // Serialiserad pipeline-data från RSC (page.tsx). Alla 10 grupper, även
  // tomma — statusbaren visar bara count > 0; sektionsvyn filtrerar lika.
  groups: PipelineGroupDto[];
  // ApplicationRow förblir server-renderbar (F3/CTO-mönster). page.tsx (RSC)
  // server-renderar raderna och passar in dem som en ReactNode[]-slot-map
  // keyad på status. Renderad ReactNode är serialiserbar över RSC→Client-
  // gränsen — en render-prop-FUNKTION är det INTE (Next.js use-client.md
  // rad 50-57; orsakade prod-incidenten i commit eece124). Client-ön slår
  // upp slots per group.status; den anropar ingen funktion och äger aldrig
  // rad-utseendet.
  rowSlots: Record<ApplicationStatus, ReactNode[]>;
}

type FilterValue = "All" | ApplicationStatus;

/**
 * Client-ö för /ansokningar-listan (v3 — HANDOVER §7.3). Arkitekturen
 * speglar F3: page.tsx förblir RSC + äger auth/error/total===0;
 * ApplicationRow förblir server-renderbar och passas som serialiserbar
 * ReactNode[]-slot-map (rowSlots) keyad på status — INGEN render-prop-
 * funktion över RSC→Client-gränsen.
 *
 * Client-ön äger EN interaktion: statusbar-filtret (v3 pages.jsx
 * AnsokningarPage `active`-state). "Alla" + per-status med count > 0.
 * Sektioner renderas per status (count > 0) i fast PIPELINE_ORDER —
 * den REALA domän-ordningen (delad från status.ts, ej v3-STATUS_ORDER-
 * mock). Ingen URL-param, ingen persistens (speglar v3-prototypen exakt).
 */
export function ApplicationsPipeline({
  groups,
  rowSlots,
}: ApplicationsPipelineProps) {
  const [active, setActive] = useState<FilterValue>("All");

  const byStatus = useMemo(
    () => new Map(groups.map((g) => [g.status, g])),
    [groups]
  );

  const total = useMemo(
    () => groups.reduce((sum, g) => sum + g.count, 0),
    [groups]
  );

  // Flikar: "Alla" + status med count > 0, i pipeline-ordning.
  const tabs: FilterValue[] = [
    "All",
    ...PIPELINE_ORDER.filter((s) => (byStatus.get(s)?.count ?? 0) > 0),
  ];

  // Synliga sektioner: count > 0 OCH (active === "All" || status === active),
  // i pipeline-ordning.
  const sections = PIPELINE_ORDER.map((status) => byStatus.get(status)).filter(
    (g): g is PipelineGroupDto =>
      g != null &&
      g.count > 0 &&
      (active === "All" || g.status === active)
  );

  return (
    <>
      <div className="jp-statusbar" role="tablist" aria-label="Status">
        {tabs.map((t) => {
          const isActive = active === t;
          const count = t === "All" ? total : byStatus.get(t)?.count ?? 0;
          const label = t === "All" ? "Alla" : getStatusLabel(t);
          return (
            <button
              key={t}
              type="button"
              role="tab"
              aria-selected={isActive}
              data-active={isActive}
              className="jp-statusbar__item"
              onClick={() => setActive(t)}
            >
              {label}
              <span className="jp-statusbar__count">{count}</span>
            </button>
          );
        })}
      </div>

      {sections.length === 0 ? (
        <div className="jp-empty">
          <div className="jp-empty__title">
            Inga ansökningar i den här statusen
          </div>
          Välj en annan flik eller skapa en ny ansökan.
        </div>
      ) : (
        sections.map((group) => {
          const label = getStatusLabel(group.status);
          return (
            <section
              key={group.status}
              id={`status-${group.status}`}
              aria-label={label}
              className="jp-section scroll-mt-6"
            >
              <div className="jp-section__head">
                <h2 className="jp-section__title">{label}</h2>
                <span className="jp-section__count">{group.count}</span>
              </div>
              <div className="jp-applist">
                {rowSlots[group.status] ?? []}
              </div>
            </section>
          );
        })
      )}
    </>
  );
}
