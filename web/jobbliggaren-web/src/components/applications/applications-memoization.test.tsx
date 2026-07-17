import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { PIPELINE_ORDER } from "@/lib/applications/status";
import type {
  ApplicationAttentionSignal,
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import { ApplicationActionsProvider } from "./application-actions";
import { ApplicationsTable } from "./applications-table";
import { ApplicationsPipeline } from "./applications-pipeline";
import { ApplicationRow } from "./application-row";
import { ApplicationsTableRow } from "./applications-table-row";
import { AttentionQueue } from "./attention-queue";
import { StepRail } from "./step-rail";

/**
 * #747 (perf-audit d1/d2) — render-count fitness function för memoiseringen av
 * ansöknings-trädet. Oraklet är BETEENDE, inte struktur: två rena per-render-
 * bieffekter räknas via modul-wrappers.
 *
 *   - `daysInStatus` anropas EXAKT en gång per renderad rad (ApplicationRow rad 93,
 *     ApplicationsTableRow rad 61) → antal anrop = antal rad-renderingar.
 *   - `getStatusVariantKey` anropas en gång per stegrail-cell → en delta > 0 på ett
 *     sök-tangenttryck betyder att StepRail re-renderade (rad-menyerna skippar via
 *     rad-memon, så deltan isolerar railen).
 *
 * Grinden är MUTATIONS-VERIFIERAD (docs/reviews/2026-07-18-747-memoization-cto.md /
 * PR-body): att ta bort någon av de sex memo/useCallback-lindningarna vänder minst
 * ett av testerna nedan till RÖTT. Ett asymmetriskt seed gör +0/+1 särskiljbart
 * från +N (≥3 rader togglas 1; ≥2 överlevande i sök-fallet) — annars vore en grön
 * grind vakuös (reference_count_only_oracle_needs_asymmetric_seed).
 */

const counters = vi.hoisted(() => ({ days: 0, variant: 0 }));

// Server actions + router-sömmarna mockas → rena presentation-tester (samma
// mönster som applications-pipeline.test.tsx / applications-table.test.tsx).
vi.mock("@/lib/actions/applications", () => ({
  batchTransitionAction: vi.fn(async () => ({ success: true as const })),
  transitionStatusAction: vi.fn(async () => ({ success: true as const })),
  logFollowUpAction: vi.fn(async () => ({ success: true as const })),
}));
vi.mock("@/lib/actions/set-applications-view-action", () => ({
  setApplicationsViewAction: vi.fn(async () => undefined),
}));
vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({
      push: vi.fn(),
      back: vi.fn(),
      refresh: vi.fn(),
      replace: vi.fn(),
      prefetch: vi.fn(),
    }),
  };
});

// Per-render-räknare: wrappar EN funktion, behåller resten av modulen verklig.
vi.mock("@/lib/applications/urgency", async (importActual) => {
  const actual =
    await importActual<typeof import("@/lib/applications/urgency")>();
  return {
    ...actual,
    daysInStatus: (...args: Parameters<typeof actual.daysInStatus>) => {
      counters.days++;
      return actual.daysInStatus(...args);
    },
  };
});
vi.mock("@/lib/applications/status", async (importActual) => {
  const actual =
    await importActual<typeof import("@/lib/applications/status")>();
  return {
    ...actual,
    getStatusVariantKey: (
      ...args: Parameters<typeof actual.getStatusVariantKey>
    ) => {
      counters.variant++;
      return actual.getStatusVariantKey(...args);
    },
  };
});

const NOW = new Date("2026-07-10T12:00:00Z");
const NOW_ISO = NOW.toISOString();

function makeApp(
  id: string,
  title: string,
  status: ApplicationStatus,
  lastStatusChangeAt: string,
  attentionSignal: ApplicationAttentionSignal = "None",
): ApplicationDto {
  return {
    id,
    jobSeekerId: "seeker-1",
    jobAdId: `ad-${id}`,
    status,
    createdAt: "2026-05-01T00:00:00Z",
    updatedAt: "2026-05-01T00:00:00Z",
    lastStatusChangeAt,
    attentionSignal,
    jobAd: {
      jobAdId: `ad-${id}`,
      title,
      company: `Företag ${title}`,
      url: null,
      source: "Manual",
      publishedAt: null,
      expiresAt: null,
    },
  };
}

// Fem tabell-rader med distinkta statusar (asymmetriskt seed: togglar EN av fem).
function fiveTableRows(): ApplicationDto[] {
  return [
    makeApp("id-a", "Alfa", "Draft", "2026-07-09T00:00:00Z"),
    makeApp("id-b", "Beta", "Submitted", "2026-07-01T00:00:00Z"),
    makeApp("id-c", "Gamma", "Acknowledged", "2026-07-05T00:00:00Z"),
    makeApp("id-d", "Delta", "Interviewing", "2026-07-08T00:00:00Z"),
    makeApp("id-e", "Epsilon", "OfferReceived", "2026-07-10T00:00:00Z"),
  ];
}

// Pipeline-grupper: `count` apps i `status`, titlar `${titleBase}-i`, valfria
// signaler positionellt alignade.
function makeGroups(
  status: ApplicationStatus,
  count: number,
  titleBase: string,
  signals: ApplicationAttentionSignal[] = [],
): PipelineGroupDto[] {
  return PIPELINE_ORDER.map((s) => ({
    status: s,
    count: s === status ? count : 0,
    applications:
      s === status
        ? Array.from({ length: count }, (_, i) =>
            makeApp(
              `${status}-${i}`,
              `${titleBase}-${i}`,
              status,
              "2026-07-01T00:00:00Z",
              signals[i] ?? "None",
            ),
          )
        : [],
  }));
}

beforeEach(() => {
  counters.days = 0;
  counters.variant = 0;
});

describe("#747 memoisering — tabell (d1)", () => {
  // OBS: absoluta daysInStatus-tal går INTE att asserta i tabellen — `days`-
  // sorteringens komparator (compareApplications) anropar daysInStatus per
  // jämförelse, så en initial rendering av N rader ger N + O(N log N) anrop.
  // 1:1-invarianten (en gång per rad-rendering) bevisas därför i list-vyn nedan
  // (ingen klient-sort där); tabellen asserterar bara toggle-DELTAN, som är ren
  // (en checkbox-toggle ändrar inte sortKey → `sorted`-useMemon är cachad → noll
  // nya komparator-anrop).
  it("en checkbox-toggle re-renderar BARA den togglade raden (memo + useCallback)", () => {
    render(
      <ApplicationActionsProvider>
        <ApplicationsTable rows={fiveTableRows()} now={NOW} />
      </ApplicationActionsProvider>,
    );
    counters.days = 0;

    // checkboxes[0] = rubrikens markera-alla; [1..5] = de fem raderna.
    const rowCheckbox = screen.getAllByRole("checkbox")[3];
    if (rowCheckbox == null) throw new Error("förväntade minst fyra checkboxar");
    fireEvent.click(rowCheckbox);

    // +1 (bara den togglade radens `selected` ändrades), aldrig +5. Utan
    // memo(ApplicationsTableRow) ELLER useCallback(toggleRow) → 5.
    expect(counters.days).toBe(1);
  });
});

describe("#747 memoisering — pipeline (d2)", () => {
  it("sök som behåller träffmängden re-renderar NOLL överlevande list-rader (memo(ApplicationRow))", () => {
    // Tre apps i Submitted (default-öppen), delad titel-substräng, inga signaler
    // → kön är tom, bara list-raderna renderar.
    render(
      <ApplicationsPipeline
        groups={makeGroups("Submitted", 3, "Utvecklare")}
        nowIso={NOW_ISO}
        initialView="lista"
      />,
    );
    // 1:1-ORAKEL-INVARIANTEN: tre list-rader → exakt tre daysInStatus-anrop
    // (ingen klient-sort i list-vyn). Detta är vad som gör +0/+1-deltana ovan
    // och nedan icke-vakuösa — utan denna koppling vore antalen meningslösa.
    expect(counters.days).toBe(3);
    counters.days = 0;

    // "utveck" matchar alla tre → ingen rad av-/på-monteras; stabila props.
    fireEvent.change(screen.getByRole("searchbox"), {
      target: { value: "utveck" },
    });

    // +0: de tre överlevande raderna skippar via memo. Utan memo(ApplicationRow) → +3.
    expect(counters.days).toBe(0);
  });

  it("sök re-renderar INTE kö-kortens rader (memo(AttentionQueue))", () => {
    // Två apps i en icke-default-öppen status (Acknowledged) med fyrande signal →
    // syns i kön; list-sektionen är kollapsad initialt (0 list-rader).
    render(
      <ApplicationsPipeline
        groups={makeGroups("Acknowledged", 2, "Alfa", [
          "OverdueFollowUp",
          "OverdueFollowUp",
        ])}
        nowIso={NOW_ISO}
        initialView="lista"
      />,
    );
    // Kön renderar 2 kort; Acknowledged-sektionen kollapsad → inga list-rader.
    expect(counters.days).toBe(2);
    counters.days = 0;

    // Icke-matchande sök → list-sektionen förblir tom; kön är sök-invariant.
    fireEvent.change(screen.getByRole("searchbox"), {
      target: { value: "zzz-ingen-traff" },
    });

    // +0: kön skippar via memo (groups/now stabila). Utan memo(AttentionQueue)
    // re-renderar kön → cardActions ger nya primaryAction → dess rader re-renderar → +2.
    expect(counters.days).toBe(0);
  });

  it("sök re-renderar INTE stegrailen (memo(StepRail) + useCallback(toggleFilter))", () => {
    render(
      <ApplicationsPipeline
        groups={makeGroups("Submitted", 3, "Utvecklare")}
        nowIso={NOW_ISO}
        initialView="lista"
      />,
    );
    // Ignorera initialbaslinjen (rail 10 celler + ev. rad-menyer) — mät deltan.
    counters.variant = 0;

    fireEvent.change(screen.getByRole("searchbox"), {
      target: { value: "utveck" },
    });

    // +0: railen skippar (memo + stabil onToggle); de överlevande radernas menyer
    // skippar via rad-memon → deltan isolerar railen. Utan memo(StepRail) ELLER
    // useCallback(toggleFilter) re-renderar railen → +PIPELINE_ORDER.length.
    expect(counters.variant).toBe(0);
  });
});

describe("#747 memoisering — struktur (sekundär närvaro-kontroll)", () => {
  it("de fyra render-tunga löven är memo-lindade", () => {
    const memoType = Symbol.for("react.memo");
    for (const Component of [
      ApplicationRow,
      ApplicationsTableRow,
      AttentionQueue,
      StepRail,
    ]) {
      expect(
        (Component as unknown as { $$typeof: symbol }).$$typeof,
      ).toBe(memoType);
    }
  });
});
