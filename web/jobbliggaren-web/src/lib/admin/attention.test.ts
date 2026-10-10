import { describe, expect, it } from "vitest";
import { backupFixture, OVERVIEW_TIME, servicesFixture } from "@/test/fixtures/admin-overview";
import type { BackupObservationData } from "@/lib/dto/admin-overview";
import type { ServicesObservationDto } from "@/lib/dto/admin-services";
import { backupAttention, backupOrServicesRaised, servicesAttention } from "./attention";
import type { AwaitingObservation, OverviewObservation } from "./overview";

const SAMPLED = "2026-10-08T09:59:30Z";

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
    expect(servicesAttention(services())).toEqual({ kind: "read", raised: [], undetermined: [], importOverdueAfterSeconds: 1500 });
  });

  it.each([
    ["a silent Worker", { backgroundJobs: { state: "Silent", sampledAt: SAMPLED, heartbeatAt: "2026-10-08T09:50:00Z" } }, "backgroundJobs", "silent"],
    ["no Worker", { backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } }, "backgroundJobs", "noServer"],
    ["a store that does not answer", { volatileStore: { state: "Unreachable", sampledAt: SAMPLED } }, "volatileStore", "unreachable"],
    ["an overdue import", { jobImport: { state: "Overdue", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:20:00Z", errors: 0 } }, "jobImport", "overdue"],
    ["no import run", { jobImport: { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: null } }, "jobImport", "noneRecorded"],
  ] as const)("raises %s", (_label, patch, row, kind) => {
    const attention = servicesAttention(services(patch));

    if (attention.kind !== "read") throw new Error("Expected read services");
    expect(attention.raised).toHaveLength(1);
    expect(attention.raised[0]).toMatchObject({ row, reading: { kind } });
    expect(attention.undetermined).toEqual([]);
  });

  it("raises nothing for a recorded run with errors: the card marks those, and the attention card lists states only", () => {
    const attention = servicesAttention(services({ jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: "2026-10-08T09:50:00Z", errors: 4 } }));

    expect(attention).toMatchObject({ kind: "read", raised: [], undetermined: [] });
  });

  it("names every undetermined row, so that none of them reads as fine", () => {
    const attention = servicesAttention(services({
      backgroundJobs: { state: "Unknown", sampledAt: null, heartbeatAt: null },
      volatileStore: { state: "Unknown", sampledAt: null },
      jobImport: { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null },
    }));

    expect(attention).toEqual({
      kind: "read", raised: [], undetermined: ["backgroundJobs", "volatileStore", "jobImport"], importOverdueAfterSeconds: 1500,
    });
  });

  it.each(["failed", "loading"] as const)("reads a %s source as unread, never as clear", (kind) => {
    expect(servicesAttention({ kind })).toEqual({ kind: "unread", why: kind });
  });
});

describe("backupOrServicesRaised", () => {
  it("is false while both cards show only positive states", () => {
    expect(backupOrServicesRaised(backup(), services())).toBe(false);
  });

  it("is true for a backup that is off, and for a silent Worker, each on its own", () => {
    expect(backupOrServicesRaised(backup({ timer: { state: "inactive" } }), services())).toBe(true);
    expect(backupOrServicesRaised(backup(), services({ backgroundJobs: { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null } }))).toBe(true);
  });

  it("is false for sources that could not be read: unread is unknown, not raised", () => {
    expect(backupOrServicesRaised({ kind: "failed" }, { kind: "loading" })).toBe(false);
    expect(backupOrServicesRaised({ kind: "awaiting" }, { kind: "failed" })).toBe(false);
  });
});
