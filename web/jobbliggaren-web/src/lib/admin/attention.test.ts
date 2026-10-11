import { describe, expect, it } from "vitest";
import { backupFixture, OVERVIEW_TIME, servicesFixture } from "@/test/fixtures/admin-overview";
import type { BackupObservationData } from "@/lib/dto/admin-overview";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { backupAttention, backupOrServicesRaised, servicesAttention } from "./attention";
import type { AwaitingObservation, OverviewObservation } from "./overview";

const SAMPLED = "2026-10-08T09:59:30Z";
/** The page's clock: the fixture's rows are 30 s old, well within the API's 120 s. */
const NOW = Date.parse(OVERVIEW_TIME);
/** The page's clock once a kept row is exactly at the limit, and one second past it. */
const AT_LIMIT = Date.parse(SAMPLED) + 120_000;
const PAST_LIMIT = Date.parse(SAMPLED) + 121_000;

const backup = (data: Partial<BackupObservationData> = {}): AwaitingObservation<BackupObservationData> =>
  ({ kind: "loaded", sampledAt: OVERVIEW_TIME, refreshFailed: false, data: { ...backupFixture(), ...data } });
const services = (patch: Partial<ServicesObservationDto> = {}): OverviewObservation<ServicesObservationDto> =>
  ({ kind: "loaded", sampledAt: SAMPLED, refreshFailed: false, data: { ...servicesFixture(), ...patch } });

describe("backupAttention", () => {
  it("raises nothing for a recorded, timely run and a scheduled timer", () => {
    expect(backupAttention(backup())).toEqual({ kind: "read", raised: [], timerUnknown: false });
  });

  it.each([
    [{ lastSuccess: { state: "missing" } }, ["lastSuccess.missing"]],
    [{ lastSuccess: { state: "unreadable" } }, ["lastSuccess.unreadable"]],
    [{ lastSuccess: { state: "invalid" } }, ["lastSuccess.invalid"]],
    [{ lastSuccess: { state: "recorded", completedAt: "2026-10-06T00:19:07+00:00", overdue: true } }, ["lastSuccess.overdue"]],
    [{ timer: { state: "inactive" } }, ["nextRun.inactive"]],
    [{ timer: { state: "notInstalled" } }, ["nextRun.notInstalled"]],
  ] as const)("raises the card's own warning for %j", (data, raised) => {
    expect(backupAttention(backup(data))).toMatchObject({ kind: "read", raised });
  });

  it("raises both of the box's present warnings: no run recorded and a timer that is off", () => {
    expect(backupAttention(backup({ lastSuccess: { state: "missing" }, timer: { state: "inactive" } })))
      .toEqual({ kind: "read", raised: ["lastSuccess.missing", "nextRun.inactive"], timerUnknown: false });
  });

  it("does not raise an unknown timer, and does not let it pass as fine either", () => {
    expect(backupAttention(backup({ timer: { state: "unknown" } }))).toEqual({ kind: "read", raised: [], timerUnknown: true });
  });

  it.each(["failed", "loading", "awaiting"] as const)("reads a %s source as unread, never as clear", (kind) => {
    expect(backupAttention({ kind })).toEqual({ kind: "unread", why: kind });
  });
});

describe("servicesAttention", () => {
  it("raises nothing and leaves nothing undetermined for three positive rows", () => {
    expect(servicesAttention(services(), NOW)).toEqual({ kind: "read", raised: [], undetermined: [], importOverdueAfterSeconds: 1500 });
  });

  it.each([
    ["a silent Worker", { backgroundJobs: { state: "Silent", sampledAt: SAMPLED, heartbeatAt: "2026-10-08T09:50:00Z" } },
      { row: "backgroundJobs", kind: "silent", heartbeatAt: "2026-10-08T09:50:00Z" }],
    ["no Worker", { backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } }, { row: "backgroundJobs", kind: "noServer" }],
    ["a store that does not answer", { volatileStore: { state: "Unreachable", sampledAt: SAMPLED } }, { row: "volatileStore", kind: "unreachable" }],
    ["an overdue import", { jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 0 } },
      { row: "jobImport", kind: "overdue", lastRunAt: "2026-10-08T09:20:00Z" }],
    ["no import run", { jobImport: { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: null } }, { row: "jobImport", kind: "noneRecorded" }],
    ["a recorded run with errors", { jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:50:00Z", errors: 4 } },
      { row: "jobImport", kind: "errors", count: 4 }],
  ] as const)("raises %s, as the Tjänster card's warning form shows it", (_label, patch, line) => {
    const attention = servicesAttention(services(patch), NOW);

    if (attention.kind !== "read") throw new Error("Expected read services");
    expect(attention.raised).toEqual([line]);
    expect(attention.undetermined).toEqual([]);
  });

  it("raises both lines of an overdue run with errors, the overdue one first, as the card shows both", () => {
    const attention = servicesAttention(services({
      jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 2 },
    }), NOW);

    expect(attention).toMatchObject({ kind: "read", raised: [
      { row: "jobImport", kind: "overdue", lastRunAt: "2026-10-08T09:20:00Z" },
      { row: "jobImport", kind: "errors", count: 2 },
    ] });
  });

  it("raises no errors line for a run without errors, since the card never says '0 fel'", () => {
    expect(servicesAttention(services(), NOW)).toMatchObject({ raised: [] });
  });

  it("names every undetermined row, so that none of them reads as fine", () => {
    const attention = servicesAttention(services({
      backgroundJobs: { state: "Unknown", sampledAt: null, heartbeatAt: null },
      volatileStore: { state: "Unknown", sampledAt: null },
      jobImport: { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null },
    }), NOW);

    expect(attention).toEqual({
      kind: "read", raised: [], undetermined: ["backgroundJobs", "volatileStore", "jobImport"], importOverdueAfterSeconds: 1500,
    });
  });

  it("still raises a kept warning whose sample is exactly the API's limit old", () => {
    const attention = servicesAttention(services({ volatileStore: { state: "Unreachable", sampledAt: SAMPLED } }), AT_LIMIT);

    expect(attention).toMatchObject({ kind: "read", raised: [{ row: "volatileStore", kind: "unreachable" }], undetermined: [] });
  });

  it("reads a kept row one second past the limit as undetermined, as the card withdraws it, and raises nothing from it", () => {
    const attention = servicesAttention(services({
      backgroundJobs: { state: "Silent", sampledAt: SAMPLED, heartbeatAt: "2026-10-08T09:50:00Z" },
      jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 3 },
    }), PAST_LIMIT);

    expect(attention).toEqual({
      kind: "read", raised: [], undetermined: ["backgroundJobs", "volatileStore", "jobImport"], importOverdueAfterSeconds: 1500,
    });
  });

  it("judges each row by its own sample: an old Worker is undetermined while a younger import still raises its errors", () => {
    const attention = servicesAttention(services({
      backgroundJobs: { state: "Silent", sampledAt: "2026-10-08T09:59:30Z", heartbeatAt: "2026-10-08T09:50:00Z" },
      volatileStore: { state: "Reachable", sampledAt: "2026-10-08T09:59:33Z" },
      jobImport: { state: "Recorded", sampledAt: "2026-10-08T09:59:38Z", lastRunAt: "2026-10-08T09:50:00Z", errors: 2 },
    }), PAST_LIMIT);

    expect(attention).toEqual({
      kind: "read", raised: [{ row: "jobImport", kind: "errors", count: 2 }], undetermined: ["backgroundJobs"], importOverdueAfterSeconds: 1500,
    });
  });

  it.each(["failed", "loading"] as const)("reads a %s source as unread, never as clear", (kind) => {
    expect(servicesAttention({ kind }, NOW)).toEqual({ kind: "unread", why: kind });
  });
});

describe("backupOrServicesRaised", () => {
  it("is false while both cards show only positive states", () => {
    expect(backupOrServicesRaised(backup(), services(), NOW)).toBe(false);
  });

  it("is true for a backup that is off, for a silent Worker, and for errors in a run, each on its own", () => {
    expect(backupOrServicesRaised(backup({ timer: { state: "inactive" } }), services(), NOW)).toBe(true);
    expect(backupOrServicesRaised(backup(), services({ backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } }), NOW)).toBe(true);
    expect(backupOrServicesRaised(backup(), services({
      jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:50:00Z", errors: 1 },
    }), NOW)).toBe(true);
  });

  it("is false for a services warning the page has withdrawn as too old", () => {
    const silent = services({ backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } });

    expect(backupOrServicesRaised(backup(), silent, AT_LIMIT)).toBe(true);
    expect(backupOrServicesRaised(backup(), silent, PAST_LIMIT)).toBe(false);
  });

  it("is false for sources that could not be read: unread is unknown, not raised", () => {
    expect(backupOrServicesRaised({ kind: "failed" }, { kind: "loading" }, NOW)).toBe(false);
    expect(backupOrServicesRaised({ kind: "awaiting" }, { kind: "failed" }, NOW)).toBe(false);
  });
});
