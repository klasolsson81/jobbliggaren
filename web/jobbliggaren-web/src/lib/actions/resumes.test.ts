import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { createTranslator } from "next-intl";
import type { ResumeContentDto } from "@/lib/types/resumes";
import svValidation from "../../../messages/sv/validation.json";
import svResumes from "../../../messages/sv/resumes.json";
import svErrors from "../../../messages/sv/errors.json";

vi.mock("@/lib/env", () => ({
  env: { BACKEND_URL: "http://test-backend" },
}));

// The action resolves both its schema (`getTranslations("validation")`) and its
// toast/error strings (`getTranslations("resumes.actions")`). In this unit-test
// (jsdom) context next-intl's server entry is unavailable, so mock it to a real,
// namespace-aware translator over the Swedish catalogs (source of truth) —
// verbatim messages keep flowing, identical to production.
vi.mock("next-intl/server", () => ({
  getTranslations: async (
    namespace?: "validation" | "resumes.actions" | "errors",
  ) =>
    createTranslator({
      locale: "sv",
      messages: { validation: svValidation, resumes: svResumes, errors: svErrors },
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

import {
  promoteParsedResumeAction,
  promoteParsedResumeFromGuideAction,
  discardParsedResumeAction,
  setFindingStatusAction,
  updateTemplateOptionsAction,
  type FindingStatusValue,
  type TemplateOptionsInput,
} from "./resumes";

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

// Guide-ingången (Fas 4b PR-8.3 → PR-8.4): samma one-shot promote som sid-ingången men
// landar på den kanoniska granska-vyn `/cv/{id}/granska` (CTO-bind Q4: guiden slutar i
// granskningen där användaren ser vad som kan åtgärdas). Delar `promoteParsedResumeCore`.
describe("promoteParsedResumeFromGuideAction", () => {
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

  it("201 → redirectar till den kanoniska granska-vyn /cv/{id}/granska (INTE hubben eller /cv/{id})", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ id: NEW_ID }), {
        status: 201,
        headers: { "Content-Type": "application/json" },
      }),
    );
    global.fetch = fetchMock;

    await expect(
      promoteParsedResumeFromGuideAction(VALID_ID, "Mitt CV", validContent),
    ).rejects.toBeInstanceOf(RedirectError);

    // /cv revalideras (nya kortet med granska-badge), men landningen är granska-vyn.
    expect(revalidatePathMock).toHaveBeenCalledWith("/cv");
    expect(redirectMock).toHaveBeenCalledWith(`/cv/${NEW_ID}/granska`);
    // Skiljer den från sid-ingången (som landar på /cv/{id}) och hubben (/cv).
    expect(redirectMock).not.toHaveBeenCalledWith(`/cv/${NEW_ID}`);
    expect(redirectMock).not.toHaveBeenCalledWith("/cv");
  });

  it("backend-fel (400) → success:false utan redirect", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "PII" }), {
        status: 400,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const result = await promoteParsedResumeFromGuideAction(
      VALID_ID,
      "Mitt CV",
      validContent,
    );

    expect(result.success).toBe(false);
    if (!result.success) expect(result.error).not.toContain("PII");
    expect(redirectMock).not.toHaveBeenCalled();
  });
});

// Hubbens åtgärdskort "Ta bort utkastet" (Fas 4b PR-8, CTO-bind Q6). POST /discard
// (soft-delete state-transition), ingen redirect — `revalidatePath("/cv")` tar bort
// kortet. `isValidId`-grinden speglar `deleteResumeAction`.
describe("discardParsedResumeAction", () => {
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

  it("avvisar ogiltigt GUID med invalidResumeId-meddelande — backend nås aldrig", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await discardParsedResumeAction("inte-en-guid");

    expect(result).toEqual({ success: false, error: "Ogiltigt CV-ID." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar utan session (notLoggedIn) — backend nås aldrig", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await discardParsedResumeAction(VALID_ID);

    expect(result).toEqual({ success: false, error: "Du är inte inloggad." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → POST:ar mot /discard, success:true och revaliderar /cv", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    const result = await discardParsedResumeAction(VALID_ID);

    expect(result).toEqual({ success: true });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/parsed/${VALID_ID}/discard`,
    );
    expect(init.method).toBe("POST");
    expect(revalidatePathMock).toHaveBeenCalledWith("/cv");
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("backend-fel (500) → success:false med mappat discardFailed (ekar ej body)", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "boom" }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const result = await discardParsedResumeAction(VALID_ID);

    expect(result).toEqual({
      success: false,
      error: "Kunde inte ta bort utkastet.",
    });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("nätverksfel → success:false med serverUnreachable", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));

    const result = await discardParsedResumeAction(VALID_ID);

    expect(result).toEqual({
      success: false,
      error: "Kunde inte nå servern. Försök igen.",
    });
  });
});

// Per-anmärkning statusskrivning (Fas 4b PR-8.4, CTO-bind Q4) — den FÖRSTA FE-konsumenten av
// PR-4:s PUT /api/v1/resumes/{id}/review/findings/{criterionId}/status. Klient-validerar
// resumeId (GUID) + status (låst mängd) + criterionId (kort alfanumerisk token) FÖRE fetch;
// vid lyckad skrivning revalideras BÅDE granska-vyn OCH /cv (ingen klient-optimism, ingen
// redirect). 400/500 mappas via `mapActionError` (ekar aldrig ProblemDetails-body:n, TD-10).
describe("setFindingStatusAction", () => {
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

  it("avvisar utan session (notLoggedIn) — backend nås aldrig", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await setFindingStatusAction(VALID_ID, "A7", "Resolved");

    expect(result).toEqual({ success: false, error: "Du är inte inloggad." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar ogiltigt resumeId (icke-GUID) med invalidResumeId — backend nås aldrig", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await setFindingStatusAction("inte-en-guid", "A7", "Resolved");

    expect(result).toEqual({ success: false, error: "Ogiltigt CV-ID." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar en status utanför den låsta mängden med invalidData — backend nås aldrig", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await setFindingStatusAction(
      VALID_ID,
      "A7",
      "Bogus" as unknown as FindingStatusValue,
    );

    expect(result).toEqual({ success: false, error: "Ogiltiga uppgifter." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar ett malformat criterionId (path-injektion-barriär) med invalidData — backend nås aldrig", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await setFindingStatusAction(VALID_ID, "A7/../x", "Resolved");

    expect(result).toEqual({ success: false, error: "Ogiltiga uppgifter." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("200 → PUT:ar {status} mot rätt URL, success:true och revaliderar BÅDE granska-vyn och /cv", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 200 }));
    global.fetch = fetchMock;

    const result = await setFindingStatusAction(VALID_ID, "A7", "Resolved");

    expect(result).toEqual({ success: true });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/${VALID_ID}/review/findings/A7/status`,
    );
    expect(init.method).toBe("PUT");
    expect(JSON.parse(init.body as string)).toEqual({ status: "Resolved" });
    expect(revalidatePathMock).toHaveBeenCalledWith(`/cv/${VALID_ID}/granska`);
    expect(revalidatePathMock).toHaveBeenCalledWith("/cv");
    // Ingen redirect — kontrollen sitter på granska-vyn, revalidering räcker.
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("backend-fel (500) → success:false med mappat statusUpdateFailed (ekar ej body), ingen revalidering", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "boom" }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const result = await setFindingStatusAction(VALID_ID, "A7", "Ignored");

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error).toBe(
        "Det gick inte att uppdatera åtgärdsstatusen. Ladda om granskningen och försök igen.",
      );
      expect(result.error).not.toContain("boom");
    }
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("backend-konflikt (409) → success:false via mapActionError (stateConflict)", async () => {
    global.fetch = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 409 }));

    const result = await setFindingStatusAction(VALID_ID, "A7", "Resolved");

    expect(result).toEqual({
      success: false,
      error: "Resursen är i ett otillåtet tillstånd. Ladda om sidan och försök igen.",
    });
  });

  it("nätverksfel → success:false med serverUnreachable", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));

    const result = await setFindingStatusAction(VALID_ID, "A7", "Open");

    expect(result).toEqual({
      success: false,
      error: "Kunde inte nå servern. Försök igen.",
    });
  });
});

// Mallbyggarens "Spara mall" (Fas 4b PR-8b 8b.3, CTO-bind) — PUT
// /api/v1/resumes/{id}/template-options. Klient-validerar resumeId (GUID) + de fyra
// optionerna (icke-tomma) FÖRE fetch; body-nycklarna matchar backendens
// ChangeTemplateOptionsBody (template/accentColor/fontPair/density) — INTE
// query-parametrarnas korta namn. `fontPair` bevaras oförändrad (TYPSNITT deferrad).
// Vid 204 revalideras hubben + detaljvyn + mall-vyn; 400/500 mappas via mapActionError
// (ekar aldrig ProblemDetails-body:n, TD-10).
describe("updateTemplateOptionsAction", () => {
  const originalFetch = global.fetch;

  const validOptions: TemplateOptionsInput = {
    template: "Klar",
    accentColor: "NavyBlue",
    fontPair: "Modern",
    density: "Normal",
  };

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

  it("avvisar utan session (notLoggedIn) — backend nås aldrig", async () => {
    getSessionIdMock.mockResolvedValue(null);
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await updateTemplateOptionsAction(VALID_ID, validOptions);

    expect(result).toEqual({ success: false, error: "Du är inte inloggad." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar ogiltigt resumeId (icke-GUID) med invalidResumeId — backend nås aldrig", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await updateTemplateOptionsAction("inte-en-guid", validOptions);

    expect(result).toEqual({ success: false, error: "Ogiltigt CV-ID." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("avvisar en tom option (klient-guard) med invalidData — backend nås aldrig", async () => {
    const fetchMock = vi.fn();
    global.fetch = fetchMock;

    const result = await updateTemplateOptionsAction(VALID_ID, {
      ...validOptions,
      template: "",
    });

    expect(result).toEqual({ success: false, error: "Ogiltiga uppgifter." });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("204 → PUT:ar {template, accentColor, fontPair, density} och revaliderar /cv, detalj + mall", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response(null, { status: 204 }));
    global.fetch = fetchMock;

    const result = await updateTemplateOptionsAction(VALID_ID, validOptions);

    expect(result).toEqual({ success: true });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(
      `http://test-backend/api/v1/resumes/${VALID_ID}/template-options`,
    );
    expect(init.method).toBe("PUT");
    // Body-nycklarna är backendens (accentColor/fontPair), inte query-parametrarnas
    // korta namn (accent/font) — den kritiska kontrakts-detaljen.
    expect(JSON.parse(init.body as string)).toEqual({
      template: "Klar",
      accentColor: "NavyBlue",
      fontPair: "Modern",
      density: "Normal",
    });
    expect(revalidatePathMock).toHaveBeenCalledWith("/cv");
    expect(revalidatePathMock).toHaveBeenCalledWith(`/cv/${VALID_ID}`);
    expect(revalidatePathMock).toHaveBeenCalledWith(`/cv/${VALID_ID}/mall`);
    expect(redirectMock).not.toHaveBeenCalled();
  });

  it("backend-fel (500) → success:false med mappat templateSaveFailed (ekar ej body), ingen revalidering", async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: "PII 19900101-1234" }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      }),
    );

    const result = await updateTemplateOptionsAction(VALID_ID, validOptions);

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error).toBe("Kunde inte spara mallen.");
      expect(result.error).not.toContain("PII");
    }
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("nätverksfel → success:false med serverUnreachable", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));

    const result = await updateTemplateOptionsAction(VALID_ID, validOptions);

    expect(result).toEqual({
      success: false,
      error: "Kunde inte nå servern. Försök igen.",
    });
  });
});
