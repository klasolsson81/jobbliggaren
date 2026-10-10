import { describe, expect, it } from "vitest";
import type { HostObservationDto } from "@/lib/dto/admin-host";
import {
  ageServerReading,
  hasStaleReading,
  newestSampledAt,
  toServerReading,
  type AdminServerReading,
} from "./host-observation";

const SAMPLED = "2026-10-10T12:00:00+00:00";
const T0 = Date.parse(SAMPLED);

const dto = (patch: Partial<HostObservationDto> = {}): HostObservationDto => ({
  readAt: "2026-10-10T12:00:10Z",
  staleAfterSeconds: 120,
  cpu: { state: "Available", sampledAt: SAMPLED, value: { percent: 5, windowSeconds: 30 } },
  memory: { state: "Available", sampledAt: SAMPLED, value: { percent: 29.4, usedBytes: 2_449_854_464, totalBytes: 8_331_255_808 } },
  disk: { state: "Available", sampledAt: SAMPLED, value: { percent: 6.1, freeBytes: 242_287_181_824, totalBytes: 258_154_033_152 } },
  ...patch,
});

describe("toServerReading", () => {
  it("turns an Available reading into a value that is not stale, with its own sample time", () => {
    const server = toServerReading(dto());

    expect(server.cpu).toEqual({ kind: "value", value: { percent: 5, windowSeconds: 30 }, sampledAt: SAMPLED, stale: false });
    expect(server.staleAfterMs).toBe(120_000);
  });

  it("keeps a Stale reading's value and marks it stale", () => {
    const server = toServerReading(dto({ disk: { ...dto().disk, state: "Stale" } }));

    expect(server.disk).toMatchObject({ kind: "value", stale: true, sampledAt: SAMPLED });
  });

  it.each([
    ["Collecting", "collecting"],
    ["NotObservable", "notObservable"],
    ["Failed", "failed"],
  ] as const)("maps %s to %s and never to a number", (state, kind) => {
    const server = toServerReading(dto({ cpu: { state, sampledAt: null, value: null } }));

    expect(server.cpu).toEqual({ kind });
  });

  it("lets each reading stand alone: one failing reading leaves the others as they are", () => {
    const server = toServerReading(dto({ memory: { state: "Failed", sampledAt: null, value: null } }));

    expect(server.memory.kind).toBe("failed");
    expect(server.cpu.kind).toBe("value");
    expect(server.disk.kind).toBe("value");
  });

  it("treats a value state whose value is missing as a failure rather than a zero", () => {
    // Unreachable through the schema, which refuses it; the mapper still must not invent a number.
    const server = toServerReading(dto({ cpu: { state: "Available", sampledAt: null, value: null } }));

    expect(server.cpu).toEqual({ kind: "failed" });
  });
});

describe("ageServerReading", () => {
  const server = (): AdminServerReading => toServerReading(dto());

  it("leaves a reading young enough as it is", () => {
    expect(ageServerReading(server(), T0 + 120_000).cpu).toMatchObject({ stale: false });
  });

  it("marks a kept reading stale once it passes the limit the API applied", () => {
    const aged = ageServerReading(server(), T0 + 120_001);

    expect(aged.cpu).toMatchObject({ kind: "value", stale: true });
    expect(aged.memory).toMatchObject({ stale: true });
    expect(aged.disk).toMatchObject({ stale: true });
  });

  it("does not turn a reading with no value into anything", () => {
    const withGap = toServerReading(dto({ cpu: { state: "Collecting", sampledAt: null, value: null } }));

    expect(ageServerReading(withGap, T0 + 1e9).cpu).toEqual({ kind: "collecting" });
  });
});

describe("newestSampledAt and hasStaleReading", () => {
  it("reports no sample time when no reading has a value", () => {
    const none = toServerReading(dto({
      cpu: { state: "Collecting", sampledAt: null, value: null },
      memory: { state: "NotObservable", sampledAt: null, value: null },
      disk: { state: "Failed", sampledAt: null, value: null },
    }));

    expect(newestSampledAt(none)).toBeNull();
    expect(hasStaleReading(none)).toBe(false);
  });

  it("reports the newest of the readings that have one", () => {
    const later = "2026-10-10T12:00:30+00:00";
    const server = toServerReading(dto({ disk: { ...dto().disk, sampledAt: later } }));

    expect(newestSampledAt(server)).toBe(later);
  });
});
