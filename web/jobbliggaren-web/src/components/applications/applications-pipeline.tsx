"use client";

import { Fragment, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  isActivePipelineStatus,
  PIPELINE_ORDER,
} from "@/lib/applications/status";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import { AttentionQueue } from "./attention-queue";
import { ApplicationsControls } from "./applications-controls";
import { StepRail } from "./step-rail";
import { StatusSection } from "./status-section";

// Sektioner öppna vid sidladdning (design 2a §5): Skickad, Intervju bokad,
// Erbjudande. Övriga kollapsade — kollaps är navigerings-/skalningsmekanismen.
const DEFAULT_OPEN_STATUSES: ReadonlySet<ApplicationStatus> = new Set<
  ApplicationStatus
>(["Submitted", "InterviewScheduled", "OfferReceived"]);

interface ApplicationsPipelineProps {
  // Serialiserad pipeline-data från RSC (page.tsx) — alla 10 grupper, även tomma,
  // var och en med backend-`count` + `applications` (var och en bär
  // `attentionSignal`). ADR 0092 D2: ren data över RSC→Client-gränsen, aldrig en
  // funktion eller ett renderat träd; ön renderar raderna själv (rowSlots borta).
  groups: PipelineGroupDto[];
  // Server-beräknad referenstidpunkt som ISO-sträng (#336-determinism). En
  // referenspunkt per request; rekonstrueras EN gång här och trådas ned till
  // raderna — aldrig new Date() per rad (ingen hydrerings-drift, testbart).
  nowIso: string;
}

/**
 * Client-ö för /ansokningar (design 2a, ADR 0092 PR 5). page.tsx förblir RSC +
 * äger auth/error/total===0. Detta är den ENDA RSC→Client-överlämningen (D2 "en
 * ö"); sub-komponenterna (AttentionQueue / ApplicationsControls / StepRail /
 * StatusSection) lever innanför klientgränsen och delar containerns state
 * (`query`, `statusFilter`) — SoC utan en gud-komponent (CTO-bind).
 *
 * Ordning uppifrån (design §3–7): "Kräver åtgärd"-kön → "Alla ansökningar"-rubrik
 * → kontrollrad (sök + filterchip) → stegrail → grupperade Lista-sektioner.
 *
 * PR 5-scope: ren presentation/navigation. Inga action-affordanser (kort-CTA +
 * radknappar → PR 7), ingen detaljpanel (→ PR 6), ingen vy-växlare (→ PR 8).
 * 2a-doktrin: kön DUPLICERAR (ADR 0092 supersederar ADR 0085 §343 MOVE) — appar
 * ligger kvar i sina statusgrupper; listan är "Alla".
 */
export function ApplicationsPipeline({
  groups,
  nowIso,
}: ApplicationsPipelineProps) {
  const tEnum = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");

  const now = useMemo(() => new Date(nowIso), [nowIso]);
  const byStatus = useMemo(
    () => new Map(groups.map((g) => [g.status, g])),
    [groups],
  );

  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<ApplicationStatus | null>(
    null,
  );

  const trimmedQuery = query.trim().toLowerCase();
  const hasSearch = trimmedQuery.length > 0;
  // Aktivt filter/sök tvingar matchande grupper öppna (design §5).
  const forceOpen = hasSearch || statusFilter != null;

  // Sök på roll + företag (klient-side v1, ADR 0092 D2 / YAGNI). En ansökan utan
  // kopplad annons matchar bara tom sökning.
  const matches = useMemo(() => {
    return (application: ApplicationDto): boolean => {
      if (!hasSearch) return true;
      const haystack =
        `${application.jobAd?.title ?? ""} ${application.jobAd?.company ?? ""}`.toLowerCase();
      return haystack.includes(trimmedQuery);
    };
  }, [hasSearch, trimmedQuery]);

  // Lista-sektioner (design §5): ALLA icke-tomma statusgrupper i pipelineordning.
  // 2a DUPLICAT-doktrin — kö-appar exkluderas INTE (listan = komplett). Aktivt
  // stegfilter begränsar till den valda statusen; sök filtrerar raderna och kan
  // tömma en grupp (då hoppas den över).
  const sections = useMemo(() => {
    return PIPELINE_ORDER.filter(
      (status) => statusFilter == null || statusFilter === status,
    )
      .map((status) => {
        const group = byStatus.get(status);
        if (group == null || group.count === 0) return null;
        const applications = group.applications.filter(matches);
        if (applications.length === 0) return null;
        return { status, applications };
      })
      .filter((s): s is NonNullable<typeof s> => s != null);
  }, [byStatus, statusFilter, matches]);

  const shownTotal = sections.reduce((sum, s) => sum + s.applications.length, 0);

  // "AVSLUT & VILANDE"-kicker före den FÖRSTA terminala/vilande sektionen — bara
  // utan aktivt stegfilter (design §5 / prototyp: filter === 'all').
  const firstTerminalStatus =
    statusFilter == null
      ? (sections.find((s) => !isActivePipelineStatus(s.status))?.status ?? null)
      : null;

  const activeFilterLabel =
    statusFilter != null ? applicationStatusLabel(tEnum, statusFilter) : null;

  const toggleFilter = (status: ApplicationStatus) => {
    setStatusFilter((current) => (current === status ? null : status));
  };

  return (
    <>
      <AttentionQueue groups={groups} now={now} />

      <section className="jp-allapps" aria-labelledby="all-apps-heading">
        <div className="jp-section__head jp-section__head--strong">
          <h2 id="all-apps-heading" className="jp-section__title">
            {tUi("all.title")}
          </h2>
          <span className="jp-section__count">{shownTotal}</span>
          <span className="jp-section__hint">{tUi("all.hint")}</span>
        </div>

        <ApplicationsControls
          query={query}
          onQueryChange={setQuery}
          activeFilterLabel={activeFilterLabel}
          onClearFilter={() => setStatusFilter(null)}
        />

        <StepRail
          groups={groups}
          statusFilter={statusFilter}
          onToggle={toggleFilter}
        />

        {sections.length === 0 ? (
          <div className="jp-allapps__empty">{tUi("all.noResults")}</div>
        ) : (
          sections.map((section) => (
            <Fragment key={section.status}>
              {section.status === firstTerminalStatus && (
                <p className="jp-allapps__restkicker jp-mono">
                  {tUi("all.terminalKicker")}
                </p>
              )}
              <StatusSection
                status={section.status}
                label={applicationStatusLabel(tEnum, section.status)}
                applications={section.applications}
                now={now}
                defaultOpen={DEFAULT_OPEN_STATUSES.has(section.status)}
                forceOpen={forceOpen}
              />
            </Fragment>
          ))
        )}
      </section>
    </>
  );
}
