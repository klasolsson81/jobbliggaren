import { describe, expect, it } from "vitest";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import {
  ageServicesReading,
  hasStaleServiceRow,
  newestServiceSample,
  servicesRegion,
  toServicesReading,
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
  it("keeps each row's verdict, the instants it rests on and its sample time, young", () => {
    const services = toServicesReading(dto());

    expect(services.backgroundJobs).toEqual({ kind: "alive", heartbeatAt: HEARTBEAT, sampledAt: SAMPLED, stale: false });
    expect(services.volatileStore).toEqual({ kind: "reachable", sampledAt: SAMPLED, stale: false });
    expect(services.jobImport).toEqual({ kind: "recorded", lastRunAt: RUN, errors: 0, sampledAt: SAMPLED, stale: false });
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

describe("ageServicesReading", () => {
  it("leaves a sample young enough as it is", () => {
    const aged = ageServicesReading(toServicesReading(dto()), T0 + 120_000);

    expect(aged.backgroundJobs).toMatchObject({ stale: false });
    expect(aged.jobImport).toMatchObject({ stale: false });
  });

  it("marks a kept sample old once it passes the limit the API applied, and keeps its verdict", () => {
    const aged = ageServicesReading(toServicesReading(dto()), T0 + 120_001);

    expect(aged.backgroundJobs).toMatchObject({ kind: "alive", stale: true, heartbeatAt: HEARTBEAT });
    expect(aged.volatileStore).toMatchObject({ kind: "reachable", stale: true });
    expect(aged.jobImport).toMatchObject({ kind: "recorded", stale: true });
  });

  it("leaves an undetermined row undetermined however long the page keeps it", () => {
    const aged = ageServicesReading(toServicesReading(dto(allUnknown())), T0 + 1e9);

    expect(aged.backgroundJobs).toEqual({ kind: "unknown" });
    expect(aged.volatileStore).toEqual({ kind: "unknown" });
    expect(aged.jobImport).toEqual({ kind: "unknown" });
  });
});

describe("newestServiceSample and hasStaleServiceRow", () => {
  it("reports no sample time when every row is unknown", () => {
    const none = toServicesReading(dto(allUnknown()));

    expect(newestServiceSample(none)).toBeNull();
    expect(hasStaleServiceRow(none)).toBe(false);
  });

  it("reports the newest of the rows that have one", () => {
    const later = "2026-10-10T12:00:30+00:00";
    const services = toServicesReading(dto({ volatileStore: { state: "Reachable", sampledAt: later } }));

    expect(newestServiceSample(services)).toBe(later);
  });
});

describe("servicesRegion", () => {
  it("is loaded, dated by the newest sample, and young without a clock (the preview)", () => {
    expect(servicesRegion(toServicesReading(dto()))).toMatchObject({ kind: "loaded", sampledAt: SAMPLED, stale: false });
  });

  it("is old once the page's clock passes the limit", () => {
    expect(servicesRegion(toServicesReading(dto()), T0 + 120_001)).toMatchObject({ kind: "loaded", stale: true });
  });

  it("carries no sample time when no row has one", () => {
    const region = servicesRegion(toServicesReading(dto(allUnknown())), T0);

    expect(region.kind).toBe("loaded");
    expect(region).not.toHaveProperty("sampledAt");
  });
});
