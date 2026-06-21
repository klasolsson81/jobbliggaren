import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createTranslator } from "next-intl";
import type { ResumeContentDto } from "@/lib/types/resumes";
import svValidation from "../../../messages/sv/validation.json";
import svResumes from "../../../messages/sv/resumes.json";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

// The action resolves both its schema (`getTranslations("validation")`) and its
// toast/error strings (`getTranslations("resumes.actions")`). In this unit-test
// (jsdom) context next-intl's server entry is unavailable, so mock it to a real,
// namespace-aware translator over the Swedish catalogs (source of truth) —
// verbatim messages keep flowing, identical to production.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { validation: svValidation, resumes: svResumes },
      namespace,
    }),
}));

const { getSessionIdMock } = vi.hoisted(() => ({
  getSessionIdMock: vi.fn<() => Promise<string | null>>(),
}));
vi.mock("@/lib/auth/session", () => ({ getSessionId: getSessionIdMock }));

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

import { promoteParsedResumeAction } from "./resumes";

const VALID_ID = "11111111-1111-4111-8111-111111111111";
const NEW_ID = "22222222-2222-4222-8222-222222222222";

const validContent: ResumeContentDto = {
  personalInfo: {
    fullName: "Anna Andersson",
    email: null,
    phone: null,
    location: null,
  },
  experiences: [
    {
      company: "Acme AB",
      role: "Utvecklare",
      startDate: "2021-01-01",
      endDate: null,
      description: null,
    },
  ],
  educations: [],
  skills: [],
  summary: null,
};

describe("promoteParsedResumeAction", () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    getSessionIdMock.mockResolvedValue("sess-1");
  });
  afterEach(() => {
    global.fetch = originalFetch;
    vi.restoreAllMocks();
    getSessionIdMock.mockReset();
    revalidatePathMock.mockReset();
    redirectMock.mockClear();
  });

  it("avvisar utan session — backend nås aldrig", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await promoteParsedResumeAction(VALID_ID, "Mitt CV", validContent);

    expect(result).toEqual({ success: false, error: "Du är inte inloggad." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar tomt namn (klient-validering) utan att nå backend", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await promoteParsedResumeAction(VALID_ID, "  ", validContent);

    expect(result.success).toBe(false);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar ogiltigt parsedResumeId (icke-GUID) utan att nå backend", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await promoteParsedResumeAction("xxx", "Mitt CV", validContent);

    expect(result.success).toBe(false);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("201 → POST:ar {name, content} till promote-endpoint och redirectar till nya CV:t", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: NEW_ID }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      }),
    );
    global.fetch = fetchMock;

    await expect(
      promoteParsedResumeAction(VALID_ID, "Mitt CV", validContent),
    ).rejects.toBeInstanceOf(RedirectError);

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/parsed/${VALID_ID}/promote`,
    );
    expect(init.method).toBe("POST");
    const body = JSON.parse(init.body as string) as {
      name: string;
      content: ResumeContentDto;
    };
    expect(body.name).toBe("Mitt CV");
    expect(body.content.personalInfo.fullName).toBe("Anna Andersson");
    expect(body.content.experiences[0]?.startDate).toBe("2021-01-01");

    expect(revalidatePathMock).toHaveBeenCalledWith("/cv");
    expect(redirectMock).toHaveBeenCalledWith(`/cv/${NEW_ID}`);
  });

  it("backend-fel (400) → success:false med mappat meddelande (ekar ej body)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "PII" }), {
        status: 400,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const result = await promoteParsedResumeAction(VALID_ID, "Mitt CV", validContent);

    expect(result.success).toBe(false);
    if (!result.success) expect(result.error).not.toContain("PII");
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("nätverksfel → success:false", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));

    const result = await promoteParsedResumeAction(VALID_ID, "Mitt CV", validContent);

    expect(result).toEqual({
      success: false,
      error: "Kunde inte nå servern. Försök igen.",
    });
  });
});
