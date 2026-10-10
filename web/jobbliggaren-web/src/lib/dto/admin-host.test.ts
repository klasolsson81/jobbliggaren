import { describe, expect, it } from "vitest";
import { hostObservationSchema } from "./admin-host";

const SAMPLED = "2026-10-10T12:00:00+00:00";
const READ = "2026-10-10T12:00:10Z";

// The body of GET /api/v1/admin/overview/host as the backend serialises it (GetHostObservationQueryHandlerTests:
// string states, explicit nulls, camelCase), built from the numbers of the production box on 2026-10-10.
const healthy = () => ({
  readAt: READ,
  staleAfterSeconds: 120,
  cpu: { state: "Available", sampledAt: SAMPLED, value: { percent: 5, windowSeconds: 30 } },
  memory: { state: "Available", sampledAt: SAMPLED, value: { percent: 29.4, usedBytes: 2_449_854_464, totalBytes: 8_331_255_808 } },
  disk: { state: "Available", sampledAt: SAMPLED, value: { percent: 6.1, freeBytes: 242_287_181_824, totalBytes: 258_154_033_152 } },
});

describe("hostObservationSchema", () => {
  it("accepts a healthy body", () => {
    expect(hostObservationSchema.safeParse(healthy()).success).toBe(true);
  });

  it.each([
    ["Collecting"],
    ["NotObservable"],
    ["Failed"],
  ] as const)("accepts a %s reading, which carries no value and no time", (state) => {
    const body = { ...healthy(), cpu: { state, sampledAt: null, value: null } };
    expect(hostObservationSchema.safeParse(body).success).toBe(true);
  });

  it("accepts a Stale reading that keeps its value and its original time", () => {
    const body = { ...healthy(), disk: { ...healthy().disk, state: "Stale" } };
    expect(hostObservationSchema.safeParse(body).success).toBe(true);
  });

  it.each([
    ["an Available reading without a value", { state: "Available", sampledAt: SAMPLED, value: null }],
    ["an Available reading without a time", { state: "Available", sampledAt: null, value: { percent: 1, windowSeconds: 30 } }],
    ["a Failed reading that still carries a value", { state: "Failed", sampledAt: null, value: { percent: 0, windowSeconds: 30 } }],
    ["a Collecting reading that carries a time", { state: "Collecting", sampledAt: SAMPLED, value: null }],
    ["a state the backend does not define", { state: "Unavailable", sampledAt: null, value: null }],
  ])("refuses %s", (_label, cpu) => {
    expect(hostObservationSchema.safeParse({ ...healthy(), cpu }).success).toBe(false);
  });

  it.each([
    ["a percent above 100", { ...healthy().memory, value: { percent: 100.1, usedBytes: 1, totalBytes: 2 } }],
    ["used memory above the total", { ...healthy().memory, value: { percent: 50, usedBytes: 3, totalBytes: 2 } }],
    ["a zero total", { ...healthy().memory, value: { percent: 0, usedBytes: 0, totalBytes: 0 } }],
    ["a fractional byte count", { ...healthy().memory, value: { percent: 50, usedBytes: 1.5, totalBytes: 2 } }],
  ])("refuses %s", (_label, memory) => {
    expect(hostObservationSchema.safeParse({ ...healthy(), memory }).success).toBe(false);
  });

  it("refuses free disk above the total", () => {
    const disk = { ...healthy().disk, value: { percent: 1, freeBytes: 10, totalBytes: 5 } };
    expect(hostObservationSchema.safeParse({ ...healthy(), disk }).success).toBe(false);
  });

  it("refuses a body without the read time", () => {
    const body: Record<string, unknown> = { ...healthy() };
    delete body.readAt;
    expect(hostObservationSchema.safeParse(body).success).toBe(false);
  });

  it("drops every field the contract does not name, so the browser's copy cannot grow", () => {
    const body = { ...healthy(), mount: "/", device: "vda4", cpu: { ...healthy().cpu, reason: "ReadFailed" } };

    const parsed = hostObservationSchema.parse(body);

    expect(Object.keys(parsed).sort()).toEqual(["cpu", "disk", "memory", "readAt", "staleAfterSeconds"]);
    expect(Object.keys(parsed.cpu).sort()).toEqual(["sampledAt", "state", "value"]);
  });
});
