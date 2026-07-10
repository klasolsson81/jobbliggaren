import { describe, it, expect } from "vitest";
import { maxCreatedAt } from "./seen-window";

describe("maxCreatedAt (#759 seen-window)", () => {
  it("tom lista → undefined (backend faller tillbaka på klock-nu)", () => {
    expect(maxCreatedAt([])).toBeUndefined();
  });

  it("relevanssorterad sida → MAX, inte items[0] (kärnan i #759)", () => {
    // Relevanssortering: den nyaste annonsen ligger INTE först. items[0] vore fel.
    const items = [
      { createdAt: "2026-06-20T09:00:00Z" }, // mest relevant, äldre
      { createdAt: "2026-06-28T09:00:00Z" }, // nyast
      { createdAt: "2026-06-24T09:00:00Z" },
    ];
    expect(maxCreatedAt(items)).toBe("2026-06-28T09:00:00Z");
    // items[0] is the most-relevant-but-older ad — the max must NOT be it.
    expect(maxCreatedAt(items)).not.toBe("2026-06-20T09:00:00Z");
  });

  it("nyast-först-sorterad sida → items[0] (max sammanfaller med första)", () => {
    const items = [
      { createdAt: "2026-06-28T09:00:00Z" },
      { createdAt: "2026-06-24T09:00:00Z" },
    ];
    expect(maxCreatedAt(items)).toBe("2026-06-28T09:00:00Z");
  });

  it("bär den ORIGINALA strängen med full precision (ingen ms-truncation)", () => {
    const items = [
      { createdAt: "2026-06-24T09:00:00.123456+02:00" },
      { createdAt: "2026-06-20T09:00:00Z" },
    ];
    // Originalsträngen returneras verbatim (inte en Date.parse-normaliserad form).
    expect(maxCreatedAt(items)).toBe("2026-06-24T09:00:00.123456+02:00");
  });

  it("oparsbara createdAt hoppas över; det parsbara max:et vinner", () => {
    const items = [
      { createdAt: "inte-ett-datum" },
      { createdAt: "2026-06-24T09:00:00Z" },
      { createdAt: "" },
    ];
    expect(maxCreatedAt(items)).toBe("2026-06-24T09:00:00Z");
  });

  it("alla createdAt oparsbara → undefined", () => {
    const items = [{ createdAt: "x" }, { createdAt: "" }];
    expect(maxCreatedAt(items)).toBeUndefined();
  });
});
