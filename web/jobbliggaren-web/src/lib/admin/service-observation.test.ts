import { describe, expect, it } from "vitest";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import type { JpFormatter } from "@/lib/i18n/format";
import {
  formatServiceLimit,
  hasOldServiceRow,
  newestServiceSample,
  servicesAt,
  servicesRegion,
  toServicesReading,
  withdrawOldServiceRows,
} from "./service-observation";

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

const allUnknown = (): Partial<ServicesObservationDto> => ({
  backgroundJobs: { state: "Unknown", sampledAt: null, heartbeatAt: null },
  volatileStore: { state: "Unknown", sampledAt: null },
  jobImport: { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null },
});

describe("toServicesReading", () => {
  it("keeps each row's verdict, the instants it rests on and its sample time", () => {
    const services = toServicesReading(dto());

    expect(services.backgroundJobs).toEqual({ kind: "alive", heartbeatAt: HEARTBEAT, sampledAt: SAMPLED });
    expect(services.volatileStore).toEqual({ kind: "reachable", sampledAt: SAMPLED });
    expect(services.jobImport).toEqual({ kind: "recorded", lastRunAt: RUN, errors: 0, sampledAt: SAMPLED });
    expect(services.staleAfterMs).toBe(120_000);
    expect(services.importOverdueAfterSeconds).toBe(1500);
  });

  it.each([
    ["Silent", { state: "Silent", sampledAt: SAMPLED, heartbeatAt: HEARTBEAT }, { kind: "silent", heartbeatAt: HEARTBEAT }],
    ["NoServer", { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null }, { kind: "noServer" }],
    ["Unknown", { state: "Unknown", sampledAt: null, heartbeatAt: null }, { kind: "unknown" }],
  ] as const)("maps a %s Worker", (_state, backgroundJobs, expected) => {
    expect(toServicesReading(dto({ backgroundJobs })).backgroundJobs).toMatchObject(expected);
  });

  it.each([
    ["Unreachable", { state: "Unreachable", sampledAt: SAMPLED }, { kind: "unreachable" }],
    ["Unknown", { state: "Unknown", sampledAt: null }, { kind: "unknown" }],
  ] as const)("maps a %s store", (_state, volatileStore, expected) => {
    expect(toServicesReading(dto({ volatileStore })).volatileStore).toMatchObject(expected);
  });

  it.each([
    ["Overdue", { state: "Overdue", sampledAt: SAMPLED, lastRunAt: RUN, errors: 2 }, { kind: "overdue", lastRunAt: RUN, errors: 2 }],
    ["NoneRecorded", { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: null }, { kind: "noneRecorded" }],
    ["Unknown", { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null }, { kind: "unknown" }],
  ] as const)("maps a %s import", (_state, jobImport, expected) => {
    expect(toServicesReading(dto({ jobImport })).jobImport).toMatchObject(expected);
  });

  it("lets each row stand alone: one undetermined row leaves the others as they are", () => {
    const services = toServicesReading(dto({ volatileStore: { state: "Unknown", sampledAt: null } }));

    expect(services.volatileStore.kind).toBe("unknown");
    expect(services.backgroundJobs.kind).toBe("alive");
    expect(services.jobImport.kind).toBe("recorded");
  });

  // Unreachable through the schema, which refuses each of these shapes (admin-services.test.ts); the mapper still
  // reads them as undetermined rather than inventing a time.
  it.each([
    ["an Alive Worker without a heartbeat", "backgroundJobs", { backgroundJobs: { state: "Alive", sampledAt: SAMPLED, heartbeatAt: null } }],
    ["a Reachable store without a sample time", "volatileStore", { volatileStore: { state: "Reachable", sampledAt: null } }],
    ["a Recorded import without a run time", "jobImport", { jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: null, errors: 0 } }],
  ] as const)("reads %s as unknown", (_label, row, patch) => {
    expect(toServicesReading(dto(patch))[row]).toEqual({ kind: "unknown" });
  });
});

/**
 * Three rows sampled 3 s and 8 s apart, as one tick can stamp them (each source is stamped after its own read, up to
 * about twice the probe timeout apart): the Worker at T, the store at T+3 s, the import at T+8 s.
 */
const spread = () => toServicesReading(dto({
  backgroundJobs: { state: "Alive", sampledAt: "2026-10-10T12:00:00+00:00", heartbeatAt: HEARTBEAT },
  volatileStore: { state: "Reachable", sampledAt: "2026-10-10T12:00:03+00:00" },
  jobImport: { state: "Recorded", sampledAt: "2026-10-10T12:00:08+00:00", lastRunAt: RUN, errors: 0 },
}));

describe("withdrawOldServiceRows", () => {
  it("keeps a row whose own sample is exactly the API's limit old", () => {
    const rows = withdrawOldServiceRows(toServicesReading(dto()), T0 + 120_000);

    expect(rows.backgroundJobs).toEqual({ kind: "alive", heartbeatAt: HEARTBEAT, sampledAt: SAMPLED });
    expect(rows.volatileStore).toEqual({ kind: "reachable", sampledAt: SAMPLED });
    expect(rows.jobImport).toEqual({ kind: "recorded", lastRunAt: RUN, errors: 0, sampledAt: SAMPLED });
  });

  it("withdraws every row one second past the limit: unknown, with no verdict and no time", () => {
    const rows = withdrawOldServiceRows(toServicesReading(dto()), T0 + 121_000);

    expect(rows.backgroundJobs).toEqual({ kind: "unknown" });
    expect(rows.volatileStore).toEqual({ kind: "unknown" });
    expect(rows.jobImport).toEqual({ kind: "unknown" });
    expect(rows.staleAfterMs).toBe(120_000);
  });

  it.each([
    ["a silent Worker", { backgroundJobs: { state: "Silent", sampledAt: SAMPLED, heartbeatAt: HEARTBEAT } }, "backgroundJobs"],
    ["a store that does not answer", { volatileStore: { state: "Unreachable", sampledAt: SAMPLED } }, "volatileStore"],
    ["an overdue run with errors", { jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: RUN, errors: 3 } }, "jobImport"],
  ] as const)("withdraws %s as well: an old warning is no more a verdict than an old positive", (_label, patch, row) => {
    expect(withdrawOldServiceRows(toServicesReading(dto(patch)), T0 + 121_000)[row]).toEqual({ kind: "unknown" });
  });

  it("judges each row by its own sample: at T+121 s the Worker (T) goes, the store (T+3 s) and the import (T+8 s) stay", () => {
    const rows = withdrawOldServiceRows(spread(), T0 + 121_000);

    expect(rows.backgroundJobs).toEqual({ kind: "unknown" });
    expect(rows.volatileStore).toMatchObject({ kind: "reachable" });
    expect(rows.jobImport).toMatchObject({ kind: "recorded", sampledAt: "2026-10-10T12:00:08+00:00" });
  });

  it("leaves an undetermined row undetermined however long the page keeps it", () => {
    const rows = withdrawOldServiceRows(toServicesReading(dto(allUnknown())), T0 + 1e9);

    expect(rows.backgroundJobs).toEqual({ kind: "unknown" });
    expect(rows.volatileStore).toEqual({ kind: "unknown" });
    expect(rows.jobImport).toEqual({ kind: "unknown" });
  });
});

describe("newestServiceSample and hasOldServiceRow", () => {
  it("reports no sample time and no old row when every row is unknown", () => {
    const none = toServicesReading(dto(allUnknown()));

    expect(newestServiceSample(none)).toBeNull();
    expect(hasOldServiceRow(none, T0 + 1e9)).toBe(false);
  });

  it("reports the newest of the rows that have one", () => {
    expect(newestServiceSample(spread())).toBe("2026-10-10T12:00:08+00:00");
  });

  it("finds an old row by its own sample, not by the newest", () => {
    expect(hasOldServiceRow(spread(), T0 + 120_000)).toBe(false);
    expect(hasOldServiceRow(spread(), T0 + 121_000)).toBe(true);
  });
});

describe("servicesRegion", () => {
  it("is loaded, dated by the newest sample, and young without a clock (the preview), which withdraws nothing", () => {
    const region = servicesRegion(toServicesReading(dto()));

    expect(region).toMatchObject({ kind: "loaded", sampledAt: SAMPLED, stale: false });
    if (region.kind !== "loaded") throw new Error("Expected a loaded region");
    expect(region.data.backgroundJobs.kind).toBe("alive");
  });

  it("keeps its rows and is young at exactly the limit", () => {
    const region = servicesRegion(toServicesReading(dto()), T0 + 120_000);

    expect(region).toMatchObject({ kind: "loaded", sampledAt: SAMPLED, stale: false });
    if (region.kind !== "loaded") throw new Error("Expected a loaded region");
    expect(region.data.jobImport.kind).toBe("recorded");
  });

  it("one second past the limit withdraws every row and still says when they were sampled and that it is old", () => {
    const region = servicesRegion(toServicesReading(dto()), T0 + 121_000);

    expect(region).toMatchObject({ kind: "loaded", sampledAt: SAMPLED, stale: true });
    if (region.kind !== "loaded") throw new Error("Expected a loaded region");
    expect([region.data.backgroundJobs, region.data.volatileStore, region.data.jobImport]).toEqual([
      { kind: "unknown" }, { kind: "unknown" }, { kind: "unknown" },
    ]);
  });

  it("reads its time and its age from the rows before the withdrawal: newest T+8 s, old because the Worker is", () => {
    const region = servicesRegion(spread(), T0 + 121_000);

    expect(region).toMatchObject({ kind: "loaded", sampledAt: "2026-10-10T12:00:08+00:00", stale: true });
    if (region.kind !== "loaded") throw new Error("Expected a loaded region");
    expect(region.data.backgroundJobs).toEqual({ kind: "unknown" });
    expect(region.data.jobImport.kind).toBe("recorded");
  });

  it("carries no sample time when no row has one", () => {
    const region = servicesRegion(toServicesReading(dto(allUnknown())), T0);

    expect(region.kind).toBe("loaded");
    expect(region).not.toHaveProperty("sampledAt");
  });
});

describe("servicesAt", () => {
  it("withdraws as the card does when there is a clock, and not without one", () => {
    expect(servicesAt(dto(), T0 + 121_000).backgroundJobs).toEqual({ kind: "unknown" });
    expect(servicesAt(dto(), T0 + 120_000).backgroundJobs.kind).toBe("alive");
    expect(servicesAt(dto(), undefined).backgroundJobs.kind).toBe("alive");
  });
});

describe("formatServiceLimit", () => {
  // The next-intl formatter, with the Swedish locale it resolves in production.
  const sv = {
    number: (value: number, options?: Intl.NumberFormatOptions) => new Intl.NumberFormat("sv-SE", options).format(value),
    dateTime: () => "",
  } as unknown as JpFormatter;

  it.each([
    [1500, "25\u00A0minuter"],
    [120, "2\u00A0minuter"],
    [90, "1,5\u00A0minuter"],
  ])("words %d s with the number and its unit on one line", (seconds, text) => {
    expect(formatServiceLimit(sv, seconds)).toBe(text);
  });
});
