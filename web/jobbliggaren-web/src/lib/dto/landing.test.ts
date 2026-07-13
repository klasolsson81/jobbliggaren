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

  it("parsar unknown-shape: nulla räknor, isStale=true (CTO-bind 2026-07-13, A′)", () => {
    // Detta test hette "floor-shape" och matade in activeCount: 40 000 — det beskrev en wire-shape som
    // inte längre finns, och pinnade därmed golvet. Den ENDA nya shapen på kontraktsgränsen är den här:
    // "vi vet inte" = null, aldrig en påhittad siffra. Nycklarna måste vara NÄRVARANDE med null
    // (`.nullable()` kräver det — en utelämnad nyckel hade fallit parsningen).
    const wire = {
      activeCount: null,
      newToday: null,
      isStale: true,
      refreshedAt: null,
    };
    const parsed = landingStatsDtoSchema.parse(wire);
    expect(parsed.activeCount).toBeNull();
    expect(parsed.newToday).toBeNull();
    expect(parsed.isStale).toBe(true);
    expect(parsed.refreshedAt).toBeNull();
  });

  it("parsar en MÄTT nolla som 0, inte som null (0 och null är olika svar)", () => {
    const parsed = landingStatsDtoSchema.parse({
      activeCount: 40_281,
      newToday: 0,
      isStale: false,
      refreshedAt: "2026-07-13T06:00:00+00:00",
    });
    expect(parsed.newToday).toBe(0);
    expect(parsed.newToday).not.toBeNull();
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
