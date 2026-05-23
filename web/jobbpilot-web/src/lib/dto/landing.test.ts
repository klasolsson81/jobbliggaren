import { describe, it, expect } from "vitest";
import { landingStatsDtoSchema } from "./landing";

describe("landingStatsDtoSchema (ADR 0064)", () => {
  it("parsar Worker-cache-hit-shape (isStale=false, refreshedAt satt)", () => {
    const wire = {
      activeCount: 12_345,
      newToday: 67,
      isStale: false,
      refreshedAt: "2026-05-23T12:00:00+00:00",
    };
    const parsed = landingStatsDtoSchema.parse(wire);
    expect(parsed.activeCount).toBe(12_345);
    expect(parsed.newToday).toBe(67);
    expect(parsed.isStale).toBe(false);
    expect(parsed.refreshedAt).toBe("2026-05-23T12:00:00+00:00");
  });

  it("parsar floor-shape (isStale=true, refreshedAt=null)", () => {
    const wire = {
      activeCount: 40_000,
      newToday: 0,
      isStale: true,
      refreshedAt: null,
    };
    const parsed = landingStatsDtoSchema.parse(wire);
    expect(parsed.isStale).toBe(true);
    expect(parsed.refreshedAt).toBeNull();
  });

  it("avvisar negativa räknor (backend-invariant)", () => {
    const wire = {
      activeCount: -1,
      newToday: 0,
      isStale: false,
      refreshedAt: null,
    };
    expect(() => landingStatsDtoSchema.parse(wire)).toThrow();
  });

  it("avvisar saknat refreshedAt-fält (måste vara string eller null)", () => {
    const wire = {
      activeCount: 1,
      newToday: 1,
      isStale: false,
      // refreshedAt: saknas
    };
    expect(() => landingStatsDtoSchema.parse(wire)).toThrow();
  });
});
