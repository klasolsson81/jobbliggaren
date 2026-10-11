import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { servicesRegion, toServicesReading } from "@/lib/admin/service-observation";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { AdminServicesBody } from "./admin-services-body";

// 12:00 UTC is 14:00 in Stockholm on 2026-10-10 (summer time). The heartbeat is 10 s and the run 10 min before it.
const SAMPLED = "2026-10-10T12:00:00+00:00";
const T0 = Date.parse(SAMPLED);
const HEARTBEAT = "2026-10-10T11:59:50+00:00";
const RUN = "2026-10-10T11:50:00+00:00";

const dto = (patch: Partial<ServicesObservationDto> = {}): ServicesObservationDto => ({
  staleAfterSeconds: 120,
  workerAliveWithinSeconds: 120,
  importOverdueAfterSeconds: 1500,
  backgroundJobs: { state: "Alive", sampledAt: SAMPLED, heartbeatAt: HEARTBEAT },
  volatileStore: { state: "Reachable", sampledAt: SAMPLED },
  jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: RUN, errors: 0 },
  ...patch,
});

const row = (label: string) => screen.getByText(label, { selector: "dt" }).nextElementSibling as HTMLElement;
const renderBody = (patch?: Partial<ServicesObservationDto>, now = T0, refreshFailed = false) =>
  render(<AdminServicesBody region={servicesRegion(toServicesReading(dto(patch)), now)} now={now} refreshFailed={refreshFailed} />);

/** No row may claim function or colour itself as healthy: nothing on the card verifies either (ADR 0150 D2). */
function expectNoHealthClaim(container: HTMLElement) {
  expect(container.textContent).not.toMatch(/Fungerar|fungerar|OK\b|Frisk|Aktiv\b|0 fel/);
  expect(container.querySelector(".jp-statusDot, .jp-pill, [data-state]")).toBeNull();
  expect(container.innerHTML).not.toMatch(/success/);
}

describe("AdminServicesBody with every row positive", () => {
  it("names the three rows in order", () => {
    renderBody();

    expect(screen.getAllByRole("term").map((term) => term.textContent)).toEqual(["Worker", "Tillfälligt lager", "Annonsimport"]);
  });

  it("states each positive observation with its time and age, in normal ink and without a health claim", () => {
    const { container } = renderBody();

    expect(row("Worker")).toHaveTextContent(/^Senaste livstecken 2026-10-10 13:59 \(för 10 sekunder sedan\)$/);
    expect(row("Tillfälligt lager")).toHaveTextContent(/^Svarar$/);
    expect(row("Annonsimport")).toHaveTextContent(/^Senaste körning 2026-10-10 13:50 \(för 10 minuter sedan\)$/);
    expect(container.querySelector(".jp-admin-warning")).toBeNull();
    expect(within(row("Worker")).getByText("(för 10 sekunder sedan)")).toHaveClass("jp-admindl__aside", "jp-admindl__age");
    expectNoHealthClaim(container);
  });

  it("never says the Worker works or answers", () => {
    renderBody();

    expect(row("Worker").textContent).not.toMatch(/fungerar|svarar/i);
  });

  it("prints when the sources were sampled, and says nothing about age while the sample is young", () => {
    const { container } = renderBody(undefined, T0 + 120_000);

    expect(container).toHaveTextContent("Uppgift från 2026-10-10 14:00");
    expect(container.querySelector("time")).toHaveAttribute("datetime", SAMPLED);
    expect(container).not.toHaveTextContent("äldre än");
  });

  it("shows no age without a clock, as in the preview", () => {
    render(<AdminServicesBody region={servicesRegion(toServicesReading(dto()))} />);

    expect(row("Worker")).toHaveTextContent(/^Senaste livstecken 2026-10-10 13:59$/);
    expect(row("Annonsimport")).toHaveTextContent(/^Senaste körning 2026-10-10 13:50$/);
  });

  it("shows no age for an instant ahead of the page's clock rather than one in the future", () => {
    renderBody(undefined, Date.parse(HEARTBEAT) - 5_000);

    expect(row("Worker")).toHaveTextContent(/^Senaste livstecken 2026-10-10 13:59$/);
  });
});

describe("AdminServicesBody with rows that need attention", () => {
  it("says a silent Worker in the warning form, with the last sign of life", () => {
    renderBody({ backgroundJobs: { state: "Silent", sampledAt: SAMPLED, heartbeatAt: "2026-10-10T11:50:00+00:00" } });

    const warning = row("Worker").querySelector(".jp-admin-warning");
    expect(warning).toHaveTextContent(/^Inget livstecken sedan 2026-10-10 13:50$/);
    expect(row("Worker")).not.toHaveTextContent("sedan)");
    // The instant stays whole on one line, however the bold sentence wraps: no break at its space or its hyphens.
    expect(warning?.querySelector(".jp-adminusers__date")?.textContent).toBe("2026-10-10\u00A013:50");
  });

  it("says that no Worker has reported, in the warning form, and invents no time", () => {
    renderBody({ backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } });

    expect(within(row("Worker")).getByText("Inget livstecken registrerat")).toHaveClass("jp-admin-warning");
    expect(row("Worker")).not.toHaveTextContent(/\d/);
  });

  it("says a store that does not answer in the warning form", () => {
    renderBody({ volatileStore: { state: "Unreachable", sampledAt: SAMPLED } });

    expect(within(row("Tillfälligt lager")).getByText("Svarar inte")).toHaveClass("jp-admin-warning");
  });

  it("says an overdue import with its time and the limit the API applied, worded from the number", () => {
    renderBody({ jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-10T11:20:00+00:00", errors: 0 } });

    const warning = row("Annonsimport").querySelector(".jp-admin-warning");
    expect(warning).toHaveTextContent(/^Senaste körning 2026-10-10 13:20, äldre än 25 minuter$/);
    // The instant and the limit each stay whole on one line.
    expect(warning?.querySelector(".jp-adminusers__date")?.textContent).toBe("2026-10-10\u00A013:20");
    expect(warning?.textContent).toContain("25\u00A0minuter");
  });

  it("words a limit that is not whole minutes as the duration it is", () => {
    renderBody({
      importOverdueAfterSeconds: 90,
      jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-10T11:50:00+00:00", errors: 0 },
    });

    expect(row("Annonsimport")).toHaveTextContent("äldre än 1,5 minuter");
  });

  it("says that no import run is recorded, in the warning form, and invents no time", () => {
    renderBody({ jobImport: { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: null } });

    expect(within(row("Annonsimport")).getByText("Ingen körning registrerad")).toHaveClass("jp-admin-warning");
    expect(row("Annonsimport")).not.toHaveTextContent(/\d/);
  });

  it.each([
    ["Recorded", 1, "1 fel i körningen"],
    ["Recorded", 3, "3 fel i körningen"],
    ["Overdue", 2, "2 fel i körningen"],
  ] as const)("adds a warning line to a %s run with %i errors", (state, errors, words) => {
    renderBody({ jobImport: { state, sampledAt: SAMPLED, lastRunAt: RUN, errors } });

    const line = within(row("Annonsimport")).getByText(words);
    expect(line).toHaveClass("jp-admin-warning");
    expect(line.previousElementSibling?.tagName).toBe("BR");
  });

  it.each(["Recorded", "Overdue"] as const)("never says '0 fel' for a %s run without errors", (state) => {
    renderBody({ jobImport: { state, sampledAt: SAMPLED, lastRunAt: RUN, errors: 0 } });

    expect(row("Annonsimport")).not.toHaveTextContent("fel");
  });

  it("names neither Platsbanken's nor JobTech's health", () => {
    const { container } = renderBody({ jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: RUN, errors: 4 } });

    expect(container.textContent).not.toMatch(/Platsbanken|JobTech/);
  });
});

describe("AdminServicesBody with undetermined rows", () => {
  it.each([
    ["Worker", { backgroundJobs: { state: "Unknown", sampledAt: null, heartbeatAt: null } }],
    ["Tillfälligt lager", { volatileStore: { state: "Unknown", sampledAt: null } }],
    ["Annonsimport", { jobImport: { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null } }],
  ] as const)("shows an undetermined %s as an en-dash with a quiet line, and leaves the other rows standing", (label, patch) => {
    renderBody(patch);

    expect(row(label)).toHaveTextContent(/^– Kunde inte avgöras$/);
    expect(within(row(label)).getByText("Kunde inte avgöras")).toHaveClass("jp-admindl__aside");
    // Only the visible words are spoken: the dash is hidden and carries no "Uppgift saknas" of its own.
    expect(within(row(label)).getByText("–")).toHaveAttribute("aria-hidden", "true");
    expect(row(label).querySelector(".sr-only")).toBeNull();
    expect(row(label).querySelector(".jp-admin-warning")).toBeNull();
    for (const other of ["Worker", "Tillfälligt lager", "Annonsimport"].filter((name) => name !== label)) {
      expect(row(other)).not.toHaveTextContent("Kunde inte avgöras");
    }
  });

  it("prints no sample time when every row is undetermined", () => {
    const { container } = renderBody({
      backgroundJobs: { state: "Unknown", sampledAt: null, heartbeatAt: null },
      volatileStore: { state: "Unknown", sampledAt: null },
      jobImport: { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null },
    });

    expect(container.querySelector("time")).toBeNull();
    expect(container).not.toHaveTextContent("Uppgift från");
  });
});

describe("AdminServicesBody and age", () => {
  it("keeps every row, with its verdict and its age, while its sample is exactly the API's limit old", () => {
    const { container } = renderBody(undefined, T0 + 120_000);

    expect(row("Worker")).toHaveTextContent(/^Senaste livstecken 2026-10-10 13:59 \(för 2 minuter sedan\)$/);
    expect(row("Tillfälligt lager")).toHaveTextContent(/^Svarar$/);
    expect(row("Annonsimport")).toHaveTextContent(/^Senaste körning 2026-10-10 13:50 \(för 12 minuter sedan\)$/);
    expect(container).not.toHaveTextContent("äldre än");
  });

  it("withdraws every row one second past the limit, and the note still says when and that it is old", () => {
    const { container } = renderBody({ volatileStore: { state: "Unreachable", sampledAt: SAMPLED } }, T0 + 121_000);

    for (const label of ["Worker", "Tillfälligt lager", "Annonsimport"]) {
      expect(row(label)).toHaveTextContent(/^– Kunde inte avgöras$/);
    }
    expect(container.querySelector(".jp-admin-warning")).toBeNull();
    expect(container).not.toHaveTextContent(/Senaste|Svarar/);
    expect(container).toHaveTextContent("Uppgift från 2026-10-10 14:00");
    expect(container).toHaveTextContent("Uppgiften är äldre än 2 minuter.");
  });

  it("after a failed refresh that left no row to show, says the data could not be fetched, never that the last is shown", () => {
    const { container } = renderBody(undefined, T0 + 121_000, true);

    for (const label of ["Worker", "Tillfälligt lager", "Annonsimport"]) {
      expect(row(label)).toHaveTextContent(/^– Kunde inte avgöras$/);
    }
    expect(container).not.toHaveTextContent("Senaste uppgiften visas");
    const failed = screen.getByText("Uppgifterna kunde inte hämtas. Försök igen om en stund.");
    expect(failed).toHaveClass("jp-adminsoon");
    expect(failed).not.toHaveAttribute("role");
    expect(screen.queryByRole("alert")).toBeNull();
    // The note keeps when the rows were sampled and that the sample is old.
    const notes = container.querySelectorAll("p.jp-adminkpi__sub");
    expect(notes).toHaveLength(1);
    expect(notes[0]).toHaveTextContent(/^Uppgift från 2026-10-10 14:00Uppgiften är äldre än 2 minuter\.$/);
  });

  it("judges each row by its own sample: at T+121 s the Worker (T) is withdrawn and the import (T+8 s) keeps its age", () => {
    // One tick stamps each source after its own read, so the three samples can lie seconds apart.
    const { container } = renderBody({
      backgroundJobs: { state: "Alive", sampledAt: "2026-10-10T12:00:00+00:00", heartbeatAt: HEARTBEAT },
      volatileStore: { state: "Reachable", sampledAt: "2026-10-10T12:00:03+00:00" },
      jobImport: { state: "Recorded", sampledAt: "2026-10-10T12:00:08+00:00", lastRunAt: RUN, errors: 0 },
    }, T0 + 121_000);

    expect(row("Worker")).toHaveTextContent(/^– Kunde inte avgöras$/);
    expect(row("Tillfälligt lager")).toHaveTextContent(/^Svarar$/);
    expect(row("Annonsimport")).toHaveTextContent(/^Senaste körning 2026-10-10 13:50 \(för 12 minuter sedan\)$/);
    expect(container).toHaveTextContent("Uppgift från 2026-10-10 14:00");
    expect(container).toHaveTextContent("Uppgiften är äldre än 2 minuter.");
  });

  it("puts a failed refresh in the same note, between the sample time and the age, while some kept row is still shown", () => {
    // A partial withdrawal: at T+121 s the Worker (T) is withdrawn and the store (T+3 s) and the import (T+8 s) stay,
    // so "Senaste uppgiften visas" is true of the rows still shown.
    const { container } = renderBody({
      backgroundJobs: { state: "Alive", sampledAt: "2026-10-10T12:00:00+00:00", heartbeatAt: HEARTBEAT },
      volatileStore: { state: "Reachable", sampledAt: "2026-10-10T12:00:03+00:00" },
      jobImport: { state: "Recorded", sampledAt: "2026-10-10T12:00:08+00:00", lastRunAt: RUN, errors: 0 },
    }, T0 + 121_000, true);

    expect(row("Worker")).toHaveTextContent(/^– Kunde inte avgöras$/);
    expect(row("Tillfälligt lager")).toHaveTextContent(/^Svarar$/);
    const notes = container.querySelectorAll("p.jp-adminkpi__sub");
    expect(notes).toHaveLength(1);
    expect(notes[0]).toHaveTextContent(
      /^Uppgift från 2026-10-10 14:00Uppdateringen misslyckades\. Senaste uppgiften visas\.Uppgiften är äldre än 2 minuter\.$/,
    );
    expect(container).not.toHaveTextContent("Uppgifterna kunde inte hämtas");
  });

  it("says a refresh failed even when no row has a sample time to print", () => {
    const { container } = renderBody({
      backgroundJobs: { state: "Unknown", sampledAt: null, heartbeatAt: null },
      volatileStore: { state: "Unknown", sampledAt: null },
      jobImport: { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null },
    }, T0, true);

    expect(container.querySelectorAll("p.jp-adminkpi__sub")).toHaveLength(1);
    expect(container).toHaveTextContent("Uppdateringen misslyckades. Senaste uppgiften visas.");
  });
});

describe("AdminServicesBody in the region's other states", () => {
  it.each([
    [{ kind: "unavailable" } as const, "Kommer snart"],
    [{ kind: "failed" } as const, "Uppgifterna kunde inte hämtas"],
    [{ kind: "loading" } as const, "Hämtar uppgifter"],
  ])("keeps the three rows unknown and says %o", (region, text) => {
    const { container } = render(<AdminServicesBody region={region} />);

    expect(container).toHaveTextContent(text);
    for (const label of ["Worker", "Tillfälligt lager", "Annonsimport"]) {
      expect(row(label)).toHaveTextContent(/^–Uppgift saknas$/);
    }
    expect(container.querySelector("time")).toBeNull();
    expect(container.querySelector(".jp-admin-warning")).toBeNull();
    expect(screen.queryByRole("alert")).toBeNull();
  });
});
