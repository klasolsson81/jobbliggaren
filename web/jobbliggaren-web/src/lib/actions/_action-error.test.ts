import { describe, it, expect, vi } from "vitest";
import { createTranslator } from "next-intl";
import { mapActionError } from "./_action-error";
import svErrors from "../../../messages/sv/errors.json";

// Real next-intl translator scoped to the `errors` namespace (Swedish catalog =
// source of truth). In production the action passes this `t` from
// `await getTranslations("errors")`.
const t = createTranslator({
  locale: "sv",
  messages: { errors: svErrors },
  namespace: "errors",
});

function fakeResponse(status: number, body?: unknown): Response {
  return {
    status,
    json: vi.fn(async () => body ?? {}),
  } as unknown as Response;
}

describe("mapActionError", () => {
  it("mappar 401 till 'Du är inte inloggad.'", () => {
    expect(mapActionError(fakeResponse(401), "fallback", t)).toBe(
      "Du är inte inloggad."
    );
  });

  it("mappar 403 till behörighetsfel", () => {
    expect(mapActionError(fakeResponse(403), "fallback", t)).toBe(
      "Du saknar behörighet för åtgärden."
    );
  });

  it("mappar 404 till 'Resursen hittades inte.'", () => {
    expect(mapActionError(fakeResponse(404), "fallback", t)).toBe(
      "Resursen hittades inte."
    );
  });

  it("mappar 409 till otillåtet-tillstånd-text", () => {
    expect(mapActionError(fakeResponse(409), "fallback", t)).toBe(
      "Resursen är i ett otillåtet tillstånd. Ladda om sidan och försök igen."
    );
  });

  it("mappar 422 till otillåtet-tillstånd-text (samma som 409)", () => {
    expect(mapActionError(fakeResponse(422), "fallback", t)).toBe(
      "Resursen är i ett otillåtet tillstånd. Ladda om sidan och försök igen."
    );
  });

  it("mappar 429 till rate-limit-text", () => {
    expect(mapActionError(fakeResponse(429), "fallback", t)).toBe(
      "För många försök. Vänta en stund och försök igen."
    );
  });

  it("returnerar fallback för 500", () => {
    expect(mapActionError(fakeResponse(500), "Kunde inte spara ansökan.", t)).toBe(
      "Kunde inte spara ansökan."
    );
  });

  it("returnerar fallback för 502/503/504", () => {
    expect(mapActionError(fakeResponse(502), "Kunde inte spara CV:t.", t)).toBe(
      "Kunde inte spara CV:t."
    );
    expect(mapActionError(fakeResponse(503), "fallback", t)).toBe("fallback");
    expect(mapActionError(fakeResponse(504), "fallback", t)).toBe("fallback");
  });

  it("returnerar fallback för 400 (frontend Zod ska fånga ogiltiga inputs först)", () => {
    expect(mapActionError(fakeResponse(400), "Kunde inte spara.", t)).toBe(
      "Kunde inte spara."
    );
  });

  it("säkerhetsinvariant: läser aldrig body — body med 'PII leaked here' returneras inte", () => {
    const res = fakeResponse(500, { detail: "PII leaked here", title: "Internal" });
    const result = mapActionError(res, "Generisk fallback.", t);
    expect(result).toBe("Generisk fallback.");
    expect(result).not.toContain("PII");
    expect(result).not.toContain("Internal");
    expect(res.json).not.toHaveBeenCalled();
  });
});
