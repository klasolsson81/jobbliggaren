import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createTranslator } from "next-intl";
import svValidation from "../../../messages/sv/validation.json";

// createApplicationAction-payload-kontrakt: /ny-ansokan skapar ALLTID en
// manuell ansökan. Backend tar `manual: { title, company, url?, expiresAt? }`
// och INGET `source`-fält (Source struken — manuell ansökan är implicit
// Source=Manual, projiceras i read-vägen). Detta test låser kontraktet.
//
// next/navigation.redirect kastar internt (NEXT_REDIRECT) — vi mockar den
// till en igenkännbar throw och fångar den efter att fetch-payloaden
// inspekterats. Ingen etablerad repo-konvention för server-action-fetch-test
// fanns; mönstret hålls minimalt och deterministiskt.

vi.mock("@/lib/auth/session", () => ({
  getSessionId: vi.fn(async () => "sess-1"),
}));

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://backend.test" },
}));

const revalidatePathMock = vi.fn();
vi.mock("next/cache", () => ({
  revalidatePath: (p: string) => revalidatePathMock(p),
}));

class RedirectError extends Error {}
const redirectMock = vi.fn((url: string) => {
  throw new RedirectError(url);
});
vi.mock("next/navigation", () => ({
  redirect: (url: string) => redirectMock(url),
}));

// The action builds its schema via `getTranslations("validation")`. In this
// unit-test (jsdom) context next-intl's server entry is unavailable, so mock it
// to a real translator over the Swedish catalog (source of truth) — verbatim
// validation messages keep flowing, identical to production.
vi.mock("next-intl/server", () => ({
  getTranslations: async () =>
    createTranslator({
      locale: "sv",
      messages: { validation: svValidation },
      namespace: "validation",
    }),
}));

import { createApplicationAction } from "./applications";

function formDataOf(entries: Record<string, string>): FormData {
  const fd = new FormData();
  for (const [k, v] of Object.entries(entries)) fd.set(k, v);
  return fd;
}

describe("createApplicationAction payload", () => {
  beforeEach(() => {
    revalidatePathMock.mockReset();
    redirectMock.mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("sends a `manual` object and NO top-level `source` field", async () => {
    const fetchMock = vi.fn(async () => ({
      ok: true,
      json: async () => ({ id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }),
      headers: new Headers({ "content-type": "application/json" }),
    }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(
      createApplicationAction(
        null,
        formDataOf({
          title: "Backend-utvecklare",
          company: "Volvo",
          url: "https://example.com/jobb/1",
          expiresAt: "2026-06-01",
          coverLetter: "Jag söker tjänsten.",
        })
      )
    ).rejects.toBeInstanceOf(RedirectError);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const call = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const init = call[1];
    const body = JSON.parse(init.body as string) as Record<string, unknown>;

    expect(body).toHaveProperty("manual");
    expect(body.manual).toEqual({
      title: "Backend-utvecklare",
      company: "Volvo",
      url: "https://example.com/jobb/1",
      expiresAt: "2026-06-01",
    });
    expect(body).not.toHaveProperty("source");
    expect(body.coverLetter).toBe("Jag söker tjänsten.");
  });

  it("nulls optional url/expiresAt/coverLetter in the manual payload when omitted", async () => {
    const fetchMock = vi.fn(async () => ({
      ok: true,
      json: async () => ({ id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }),
      headers: new Headers({ "content-type": "application/json" }),
    }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(
      createApplicationAction(
        null,
        formDataOf({ title: "Frontend", company: "Spotify" })
      )
    ).rejects.toBeInstanceOf(RedirectError);

    const call = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    const init = call[1];
    const body = JSON.parse(init.body as string) as {
      manual: Record<string, unknown>;
      coverLetter: unknown;
    };
    expect(body.manual.url).toBeNull();
    expect(body.manual.expiresAt).toBeNull();
    expect(body.coverLetter).toBeNull();
    expect(body).not.toHaveProperty("source");
  });

  it("returns a validation error (no fetch) when title is empty", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const result = await createApplicationAction(
      null,
      formDataOf({ title: "", company: "Volvo" })
    );

    expect(result).toEqual({ success: false, error: expect.any(String) });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
