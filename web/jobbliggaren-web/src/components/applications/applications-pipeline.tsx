"use client";

import { Fragment, useMemo, useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import {
  applicationStatusLabel,
  isActivePipelineStatus,
  PIPELINE_ORDER,
} from "@/lib/applications/status";
import { applicationMatchesQuery } from "@/lib/applications/search";
import type { ApplicationsView } from "@/lib/applications/view";
import { setApplicationsViewAction } from "@/lib/actions/set-applications-view-action";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import { ApplicationActionsProvider } from "./application-actions";
import { AttentionQueue } from "./attention-queue";
import { ApplicationsControls } from "./applications-controls";
import { ApplicationsBoard } from "./applications-board";
import { ApplicationsTable } from "./applications-table";
import { StepRail } from "./step-rail";
import { StatusSection } from "./status-section";

// Sektioner öppna vid sidladdning (design 2a §5): Skickad, Intervju bokad,
// Erbjudande. Övriga kollapsade — kollaps är navigerings-/skalningsmekanismen.
const DEFAULT_OPEN_STATUSES: ReadonlySet<ApplicationStatus> = new Set<
  ApplicationStatus
>(["Submitted", "InterviewScheduled", "OfferReceived"]);

interface ApplicationsPipelineProps {
  // Serialiserad pipeline-data från RSC (page.tsx) — de ICKE-tomma statusgrupperna
  // (backend `GroupBy(Status)` utelämnar tomma statusar helt), var och en med
  // backend-`count` + `applications` (var och en bär `attentionSignal`). Ön är
  // defensiv mot en frånvarande status (`byStatus.get() ?? 0`) så en tom eller
  // gles svarsform aldrig kraschar. ADR 0092 D2: ren data över RSC→Client-gränsen,
  // aldrig en funktion eller ett renderat träd; ön renderar raderna själv (rowSlots
  // borta).
  groups: PipelineGroupDto[];
  // Server-beräknad referenstidpunkt som ISO-sträng (#336-determinism). En
  // referenspunkt per request; rekonstrueras EN gång här och trådas ned till
  // raderna — aldrig new Date() per rad (ingen hydrerings-drift, testbart).
  nowIso: string;
  // Vy-preferensen läst SSR ur cookien (ADR 0092 D7) → seedar `view` så
  // första-paint renderar rätt vy utan flash. Ren serialiserbar sträng (D2).
  initialView: ApplicationsView;
}

/**
 * Client-ö för /ansokningar (design 2a, ADR 0092 PR 5). page.tsx förblir RSC +
 * äger auth/error/total===0. Detta är den ENDA RSC→Client-överlämningen (D2 "en
 * ö"); sub-komponenterna (AttentionQueue / ApplicationsControls / StepRail /
 * StatusSection) lever innanför klientgränsen och delar containerns state
 * (`query`, `statusFilter`) — SoC utan en gud-komponent (CTO-bind).
 *
 * Ordning uppifrån (design §3–7): "Kräver åtgärd"-kön + "Alla ansökningar"-rubrik
 * + kontrollrad (sök + VY-växlare) är DELAD chrome ovanför vyn (ADR 0092 D1);
 * under den byter `view` mellan Lista (stegrail + grupperade sektioner), Tavla
 * (kanban) och Tabell (volymvy: sortering + bulk + 50/sida, PR 10). Rail +
 * filterchip döljs i Tavla; ett aktivt stegfilter ignoreras där (kolumnerna ÄR
 * översikten) — bara sök filtrerar korten (D1/§6). Tabell behåller rail + filter
 * som Lista.
 *
 * Vy-växlaren (PR 8; Tabell tillkom PR 10) + cookie-persistens (D7). Vy-växlingen
 * är en ren klient-beräkning över redan hämtad data (D2 — även Tabellens
 * sortering/paginering/urval, Option B CTO-bind 2026-07-10) — cookien skrivs
 * fire-and-forget, ingen router.refresh, ingen flash.
 * 2a-doktrin: kön DUPLICERAR (ADR 0092 supersederar ADR 0085 §343 MOVE) — appar
 * ligger kvar i sina statusgrupper; listan är "Alla".
 */
export function ApplicationsPipeline({
  groups,
  nowIso,
  initialView,
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
  // Vy seedas från SSR-propen (D7) → ingen flash. Växlingen är omedelbar ren
  // klient-state; cookien persistas fire-and-forget i en transition (INTE
  // await:ad, INGEN router.refresh) — den är bara till för nästa-paint (D2).
  const [view, setView] = useState<ApplicationsView>(initialView);
  const [, startViewPersist] = useTransition();

  const onViewChange = (next: ApplicationsView) => {
    setView(next);
    startViewPersist(() => {
      void setApplicationsViewAction(next);
    });
  };

  const trimmedQuery = query.trim().toLowerCase();
  const hasSearch = trimmedQuery.length > 0;
  // Aktivt filter/sök tvingar matchande grupper öppna (design §5).
  const forceOpen = hasSearch || statusFilter != null;

  // Sök på roll + företag (klient-side v1, ADR 0092 D2 / YAGNI) — delad SSOT med
  // Tavla-boardet via applicationMatchesQuery (DRY). En ansökan utan kopplad
  // annons matchar bara tom sökning.
  const matches = useMemo(() => {
    return (application: ApplicationDto): boolean =>
      applicationMatchesQuery(application, trimmedQuery);
  }, [trimmedQuery]);

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
    // #630 PR 7: providern äger mutations-plumbingen (transition + toast +
    // dialogerna som EN instans vardera) för radknappar, statusmeny och
    // kökortens CTA (CTO-bind: server-recompute via revalidatePath, ingen
    // optimistisk grupp-flytt).
    <ApplicationActionsProvider>
      <AttentionQueue groups={groups} now={now} />

      <section className="jp-allapps" aria-labelledby="all-apps-heading">
        <div className="jp-section__head jp-section__head--strong">
          <h2 id="all-apps-heading" className="jp-section__title">
            {tUi("all.title")}
          </h2>
          {/* I Tavla bär boardets egen verktygsrad antalet ("N ansökningar ·
              N aktiva") — undvik dubbelräkning i rubriken. */}
          {view === "lista" && (
            <>
              {/* #805 punkt 2: inline "(N)" intill rubriken — samma form som
                  status-sektionerna och Tavla-kolumnerna (3-vy-konsekvens). */}
              <span className="jp-section__count">({shownTotal})</span>
              <span className="jp-section__hint">{tUi("all.hint")}</span>
            </>
          )}
        </div>

        <ApplicationsControls
          query={query}
          onQueryChange={setQuery}
          // Filterchipen döljs i Tavla (stegfiltret ignoreras där, D1/§6).
          activeFilterLabel={view === "tavla" ? null : activeFilterLabel}
          onClearFilter={() => setStatusFilter(null)}
          view={view}
          onViewChange={onViewChange}
        />

        {view === "tavla" ? (
          <ApplicationsBoard groups={groups} now={now} query={query} />
        ) : view === "tabell" ? (
          <>
            {/* Tabell (#630 PR 10, ADR 0092 D1): behåller railen + stegfiltret
                som Lista (railen döljs bara i Tavla, design §4). Raderna är
                sections-memons redan sök- och filter-tillämpade utfall,
                plattade — Option B, ingen ny data-hämtning (CTO-bind). */}
            <StepRail
              groups={groups}
              statusFilter={statusFilter}
              onToggle={toggleFilter}
            />
            <ApplicationsTable
              rows={sections.flatMap((section) => section.applications)}
              now={now}
            />
          </>
        ) : (
          <>
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
          </>
        )}
      </section>
    </ApplicationActionsProvider>
  );
}
