import { describe, expect, it } from "vitest";
import { servicesObservationSchema } from "./admin-services";

// The wire form of System.Text.Json for a DateTimeOffset in UTC: an offset, never a Z.
const SAMPLED = "2026-10-10T12:00:00+00:00";
const EARLIER = "2026-10-10T11:59:40+00:00";

// The body of GET /api/v1/admin/overview/services as the contract states it: string states, explicit nulls, camelCase.
const healthy = () => ({
  staleAfterSeconds: 120,
  workerAliveWithinSeconds: 120,
  importOverdueAfterSeconds: 1500,
  backgroundJobs: { state: "Alive", sampledAt: SAMPLED, heartbeatAt: EARLIER },
  volatileStore: { state: "Reachable", sampledAt: SAMPLED },
  jobImport: { state: "Recorded", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: 0 },
});

const parses = (body: unknown) => servicesObservationSchema.safeParse(body).success;

describe("servicesObservationSchema accepts every state in its own shape", () => {
  it("accepts a healthy body", () => {
    expect(parses(healthy())).toBe(true);
  });

  it.each([
    ["Alive", { state: "Alive", sampledAt: SAMPLED, heartbeatAt: EARLIER }],
    ["Silent", { state: "Silent", sampledAt: SAMPLED, heartbeatAt: EARLIER }],
    ["NoServer", { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: null }],
    ["Unknown", { state: "Unknown", sampledAt: null, heartbeatAt: null }],
  ])("accepts a %s Worker row", (_state, backgroundJobs) => {
    expect(parses({ ...healthy(), backgroundJobs })).toBe(true);
  });

  it.each([
    ["Reachable", { state: "Reachable", sampledAt: SAMPLED }],
    ["Unreachable", { state: "Unreachable", sampledAt: SAMPLED }],
    ["Unknown", { state: "Unknown", sampledAt: null }],
  ])("accepts a %s store row", (_state, volatileStore) => {
    expect(parses({ ...healthy(), volatileStore })).toBe(true);
  });

  it.each([
    ["Recorded", { state: "Recorded", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: 3 }],
    ["Overdue", { state: "Overdue", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: 0 }],
    ["NoneRecorded", { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: null }],
    ["Unknown", { state: "Unknown", sampledAt: null, lastRunAt: null, errors: null }],
  ])("accepts a %s import row", (_state, jobImport) => {
    expect(parses({ ...healthy(), jobImport })).toBe(true);
  });
});

describe("servicesObservationSchema refuses a row whose fields do not match its state", () => {
  it.each([
    ["an Alive row without a sample time", { state: "Alive", sampledAt: null, heartbeatAt: EARLIER }],
    ["an Alive row without a heartbeat", { state: "Alive", sampledAt: SAMPLED, heartbeatAt: null }],
    ["a Silent row without a sample time", { state: "Silent", sampledAt: null, heartbeatAt: EARLIER }],
    ["a Silent row without a heartbeat", { state: "Silent", sampledAt: SAMPLED, heartbeatAt: null }],
    ["a NoServer row without a sample time", { state: "NoServer", sampledAt: null, heartbeatAt: null }],
    ["a NoServer row that carries a heartbeat", { state: "NoServer", sampledAt: SAMPLED, heartbeatAt: EARLIER }],
    ["an Unknown row that carries a sample time", { state: "Unknown", sampledAt: SAMPLED, heartbeatAt: null }],
    ["an Unknown row that carries a heartbeat", { state: "Unknown", sampledAt: null, heartbeatAt: EARLIER }],
    ["a state the contract does not define", { state: "Running", sampledAt: SAMPLED, heartbeatAt: EARLIER }],
    ["a heartbeat that is not an instant", { state: "Alive", sampledAt: SAMPLED, heartbeatAt: "yesterday" }],
  ])("refuses %s", (_label, backgroundJobs) => {
    expect(parses({ ...healthy(), backgroundJobs })).toBe(false);
  });

  it.each([
    ["a Reachable row without a sample time", { state: "Reachable", sampledAt: null }],
    ["an Unreachable row without a sample time", { state: "Unreachable", sampledAt: null }],
    ["an Unknown row that carries a sample time", { state: "Unknown", sampledAt: SAMPLED }],
    ["a state the contract does not define", { state: "Healthy", sampledAt: SAMPLED }],
  ])("refuses %s", (_label, volatileStore) => {
    expect(parses({ ...healthy(), volatileStore })).toBe(false);
  });

  it.each([
    ["a Recorded row without a sample time", { state: "Recorded", sampledAt: null, lastRunAt: EARLIER, errors: 0 }],
    ["a Recorded row without a run time", { state: "Recorded", sampledAt: SAMPLED, lastRunAt: null, errors: 0 }],
    ["a Recorded row without an error count", { state: "Recorded", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: null }],
    ["an Overdue row without a run time", { state: "Overdue", sampledAt: SAMPLED, lastRunAt: null, errors: 0 }],
    ["an Overdue row without an error count", { state: "Overdue", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: null }],
    ["a NoneRecorded row without a sample time", { state: "NoneRecorded", sampledAt: null, lastRunAt: null, errors: null }],
    ["a NoneRecorded row that carries a run time", { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: null }],
    ["a NoneRecorded row that carries an error count", { state: "NoneRecorded", sampledAt: SAMPLED, lastRunAt: null, errors: 0 }],
    ["an Unknown row that carries a sample time", { state: "Unknown", sampledAt: SAMPLED, lastRunAt: null, errors: null }],
    ["an Unknown row that carries an error count", { state: "Unknown", sampledAt: null, lastRunAt: null, errors: 0 }],
    ["a negative error count", { state: "Recorded", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: -1 }],
    ["a fractional error count", { state: "Recorded", sampledAt: SAMPLED, lastRunAt: EARLIER, errors: 1.5 }],
  ])("refuses %s", (_label, jobImport) => {
    expect(parses({ ...healthy(), jobImport })).toBe(false);
  });
});

describe("servicesObservationSchema refuses limits that are not positive whole seconds within a day", () => {
  it.each(["staleAfterSeconds", "workerAliveWithinSeconds", "importOverdueAfterSeconds"] as const)("%s", (key) => {
    for (const value of [0, -1, 1.5, 86_401, null, "120"]) {
      expect(parses({ ...healthy(), [key]: value }), `${key} = ${String(value)}`).toBe(false);
    }
    expect(parses({ ...healthy(), [key]: 86_400 })).toBe(true);
  });
});

describe("servicesObservationSchema keeps the browser's copy closed", () => {
  it.each([
    ["the body", { ...healthy(), serverName: "worker-1" }],
    ["the Worker row", { ...healthy(), backgroundJobs: { ...healthy().backgroundJobs, servers: 2 } }],
    ["the store row", { ...healthy(), volatileStore: { ...healthy().volatileStore, latencyMs: 4 } }],
    ["the import row", { ...healthy(), jobImport: { ...healthy().jobImport, reason: "JobTech answered 503" } }],
  ])("refuses a key the contract does not name in %s", (_where, body) => {
    expect(parses(body)).toBe(false);
  });

  it.each(["backgroundJobs", "volatileStore", "jobImport", "staleAfterSeconds"] as const)("refuses a body without %s", (key) => {
    const body: Record<string, unknown> = { ...healthy() };
    delete body[key];
    expect(parses(body)).toBe(false);
  });
});
