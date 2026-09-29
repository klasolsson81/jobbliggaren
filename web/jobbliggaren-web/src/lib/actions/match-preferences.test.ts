import { describe, it, expect, vi, beforeEach } from "vitest";
import { createTranslator } from "next-intl";
import type { ResumeListItemDto } from "@/lib/dto/resumes";
import svSettings from "../../../messages/sv/settings.json";
import svErrors from "../../../messages/sv/errors.json";
import svValidation from "../../../messages/sv/validation.json";

// getResumes + deriveOccupations är server-only-BFF:er; mocka dem så vi kan
// driva alla diskriminerade grenar utan backend/Bearer-session.
const { getResumesMock, getParsedResumeOccupationsMock, deriveOccupationsMock } =
  vi.hoisted(() => ({
    getResumesMock: vi.fn(),
    getParsedResumeOccupationsMock: vi.fn(),
    deriveOccupationsMock: vi.fn(),
  }));
vi.mock("@/lib/api/resumes", () => ({
  getResumes: getResumesMock,
  getParsedResumeOccupations: getParsedResumeOccupationsMock,
}));
vi.mock("@/lib/api/occupation-derive", () => ({
  deriveOccupations: deriveOccupationsMock,
}));
// STEG 3 / ADR 0079: skill-BFF:erna mockas så vi kan driva alla grenar.
const { searchSkillsApiMock, getParsedResumeSkillsMock } = vi.hoisted(() => ({
  searchSkillsApiMock: vi.fn(),
  getParsedResumeSkillsMock: vi.fn(),
}));
vi.mock("@/lib/api/skills", () => ({
  searchSkills: searchSkillsApiMock,
  getParsedResumeSkills: getParsedResumeSkillsMock,
}));
const { revalidatePathMock, getSessionIdMock, authedFetchMock } = vi.hoisted(() => ({
  revalidatePathMock: vi.fn(),
  getSessionIdMock: vi.fn(),
  authedFetchMock: vi.fn(),
}));
vi.mock("next/cache", () => ({ revalidatePath: revalidatePathMock }));
vi.mock("@/lib/auth/session", () => ({ getSessionId: getSessionIdMock }));
vi.mock("@/lib/http/authed-fetch", () => ({ authedFetch: authedFetchMock }));
// A real translator over the Swedish catalogue, so an assertion reads the copy the user gets.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({
      locale: "sv",
      messages: { settings: svSettings, errors: svErrors, validation: svValidation },
      // The namespace arrives as a string at runtime; the cast only spares createTranslator a
      // literal NamespaceKey, and the catalogue resolves it per call.
      namespace: namespace as never,
    }),
}));

import {
  updateMatchPreferencesAction,
  suggestOccupationsFromCvAction,
  suggestOccupationsFromParsedResumeAction,
  searchSkillsAction,
  suggestSkillsFromParsedResumeAction,
} from "./match-preferences";
import { pickPrimaryResume } from "@/components/settings/match-preferences-shared";

type UpdateInput = Parameters<typeof updateMatchPreferencesAction>[0];

const VALID_ID = "11111111-1111-4111-8111-111111111111";

function resume(over: Partial<ResumeListItemDto>): ResumeListItemDto {
  return {
    id: "r1",
    name: "CV",
    versionCount: 1,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
    isPrimary: false,
    language: "Sv",
    latestRole: null,
    sectionCount: 1,
    topSkills: [],
    openFindingCount: null,
    origin: "Import",
    template: "Klar",
    ...over,
  };
}

describe("pickPrimaryResume", () => {
  it("tom lista → null", () => {
    expect(pickPrimaryResume([])).toBeNull();
  });

  it("väljer det primära CV:t", () => {
    const picked = pickPrimaryResume([
      resume({ id: "a", isPrimary: false }),
      resume({ id: "b", isPrimary: true }),
    ]);
    expect(picked?.id).toBe("b");
  });

  it("inget primärt → senast uppdaterade (ISO-lexikografisk)", () => {
    const picked = pickPrimaryResume([
      resume({ id: "old", updatedAt: "2026-01-01T00:00:00Z" }),
      resume({ id: "new", updatedAt: "2026-06-01T00:00:00Z" }),
    ]);
    expect(picked?.id).toBe("new");
  });
});

describe("suggestOccupationsFromCvAction", () => {
  beforeEach(() => {
    getResumesMock.mockReset();
    deriveOccupationsMock.mockReset();
  });

  it("utloggad → unauthorized", async () => {
    getResumesMock.mockResolvedValue({ kind: "unauthorized" });
    expect(await suggestOccupationsFromCvAction()).toEqual({
      kind: "unauthorized",
    });
  });

  it("inget CV → noCv", async () => {
    getResumesMock.mockResolvedValue({
      kind: "ok",
      data: { items: [], totalCount: 0, page: 1, pageSize: 50 },
    });
    expect(await suggestOccupationsFromCvAction()).toEqual({ kind: "noCv" });
  });

  it("CV utan latestRole → noRole", async () => {
    getResumesMock.mockResolvedValue({
      kind: "ok",
      data: {
        items: [resume({ isPrimary: true, latestRole: null })],
        totalCount: 1,
        page: 1,
        pageSize: 50,
      },
    });
    expect(await suggestOccupationsFromCvAction()).toEqual({ kind: "noRole" });
    expect(deriveOccupationsMock).not.toHaveBeenCalled();
  });

  it("CV med roll men inga kandidater → noRole", async () => {
    getResumesMock.mockResolvedValue({
      kind: "ok",
      data: {
        items: [resume({ isPrimary: true, latestRole: "Snickare" })],
        totalCount: 1,
        page: 1,
        pageSize: 50,
      },
    });
    deriveOccupationsMock.mockResolvedValue({
      kind: "ok",
      data: { title: "Snickare", candidates: [] },
    });
    expect(await suggestOccupationsFromCvAction()).toEqual({ kind: "noRole" });
  });

  it("CV med roll och kandidater → candidates", async () => {
    getResumesMock.mockResolvedValue({
      kind: "ok",
      data: {
        items: [resume({ isPrimary: true, latestRole: "Backendutvecklare" })],
        totalCount: 1,
        page: 1,
        pageSize: 50,
      },
    });
    deriveOccupationsMock.mockResolvedValue({
      kind: "ok",
      data: {
        title: "Backendutvecklare",
        candidates: [
          {
            occupationGroupConceptId: "grp_backend",
            occupationGroupLabel: "Backendutvecklare",
          },
        ],
      },
    });
    const result = await suggestOccupationsFromCvAction();
    expect(result).toEqual({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    });
  });

  it("derive-fel → error", async () => {
    getResumesMock.mockResolvedValue({
      kind: "ok",
      data: {
        items: [resume({ isPrimary: true, latestRole: "Roll" })],
        totalCount: 1,
        page: 1,
        pageSize: 50,
      },
    });
    deriveOccupationsMock.mockResolvedValue({ kind: "error" });
    expect(await suggestOccupationsFromCvAction()).toEqual({ kind: "error" });
  });

  it("getResumes-fel → error", async () => {
    getResumesMock.mockResolvedValue({ kind: "error" });
    expect(await suggestOccupationsFromCvAction()).toEqual({ kind: "error" });
  });
});

describe("suggestOccupationsFromParsedResumeAction", () => {
  beforeEach(() => {
    getParsedResumeOccupationsMock.mockReset();
  });

  it("tomt id → noCv utan att nå backend (vakt före BFF-anrop)", async () => {
    expect(await suggestOccupationsFromParsedResumeAction("")).toEqual({
      kind: "noCv",
    });
    expect(getParsedResumeOccupationsMock).not.toHaveBeenCalled();
  });

  it("icke-sträng id → noCv utan att nå backend", async () => {
    // Runtime-vakt (typtvång): server-actions kan anropas med godtycklig input.
    expect(
      await suggestOccupationsFromParsedResumeAction(
        null as unknown as string,
      ),
    ).toEqual({ kind: "noCv" });
    expect(getParsedResumeOccupationsMock).not.toHaveBeenCalled();
  });

  it("utloggad → unauthorized", async () => {
    getParsedResumeOccupationsMock.mockResolvedValue({ kind: "unauthorized" });
    expect(await suggestOccupationsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "unauthorized",
    });
  });

  it("notFound (okänt/främmande/befordrat artefakt) → noCv", async () => {
    getParsedResumeOccupationsMock.mockResolvedValue({ kind: "notFound" });
    expect(await suggestOccupationsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "noCv",
    });
  });

  it("ok men tom proposal-lista → noRole (CV läst, inget yrke härlett)", async () => {
    getParsedResumeOccupationsMock.mockResolvedValue({ kind: "ok", data: [] });
    expect(await suggestOccupationsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "noRole",
    });
  });

  it("ok med proposals → candidates (propose-and-approve, skrivs aldrig)", async () => {
    getParsedResumeOccupationsMock.mockResolvedValue({
      kind: "ok",
      data: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    });
    expect(await suggestOccupationsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "candidates",
      candidates: [
        {
          occupationGroupConceptId: "grp_backend",
          occupationGroupLabel: "Backendutvecklare",
        },
      ],
    });
  });

  it("övrigt fel (error) → error", async () => {
    getParsedResumeOccupationsMock.mockResolvedValue({ kind: "error" });
    expect(await suggestOccupationsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "error",
    });
  });

  it("rateLimited (default-grenen) → error (lugn fel-rad, ingen läcka av status)", async () => {
    getParsedResumeOccupationsMock.mockResolvedValue({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
    expect(await suggestOccupationsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "error",
    });
  });
});

describe("searchSkillsAction (STEG 3 / ADR 0079)", () => {
  beforeEach(() => {
    searchSkillsApiMock.mockReset();
  });

  it("icke-sträng query → tom (graceful, ingen rundtur)", async () => {
    expect(
      await searchSkillsAction(null as unknown as string)
    ).toEqual({ success: true, options: [] });
    expect(searchSkillsApiMock).not.toHaveBeenCalled();
  });

  it("ok → options", async () => {
    searchSkillsApiMock.mockResolvedValue({
      kind: "ok",
      data: [{ conceptId: "skill_react", label: "React" }],
    });
    expect(await searchSkillsAction("rea")).toEqual({
      success: true,
      options: [{ conceptId: "skill_react", label: "React" }],
    });
  });

  it("utloggad → fel (notLoggedIn-nyckel)", async () => {
    searchSkillsApiMock.mockResolvedValue({ kind: "unauthorized" });
    const result = await searchSkillsAction("rea");
    expect(result.success).toBe(false);
  });

  it("rateLimited → fel (tooManyAttempts-nyckel)", async () => {
    searchSkillsApiMock.mockResolvedValue({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
    const result = await searchSkillsAction("rea");
    expect(result.success).toBe(false);
  });

  it("övrigt fel → graceful tom (söket degraderar till 'ingen träff')", async () => {
    searchSkillsApiMock.mockResolvedValue({ kind: "error" });
    expect(await searchSkillsAction("rea")).toEqual({
      success: true,
      options: [],
    });
  });
});

describe("suggestSkillsFromParsedResumeAction (STEG 3 / ADR 0079)", () => {
  beforeEach(() => {
    getParsedResumeSkillsMock.mockReset();
  });

  it("tomt id → noCv utan att nå backend", async () => {
    expect(await suggestSkillsFromParsedResumeAction("")).toEqual({
      kind: "noCv",
    });
    expect(getParsedResumeSkillsMock).not.toHaveBeenCalled();
  });

  it("utloggad → unauthorized", async () => {
    getParsedResumeSkillsMock.mockResolvedValue({ kind: "unauthorized" });
    expect(await suggestSkillsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "unauthorized",
    });
  });

  it("notFound → noCv", async () => {
    getParsedResumeSkillsMock.mockResolvedValue({ kind: "notFound" });
    expect(await suggestSkillsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "noCv",
    });
  });

  it("ok men tom lista → noRole (CV läst, inga kompetenser härledda)", async () => {
    getParsedResumeSkillsMock.mockResolvedValue({ kind: "ok", data: [] });
    expect(await suggestSkillsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "noRole",
    });
  });

  it("ok med kandidater → candidates (med labels)", async () => {
    getParsedResumeSkillsMock.mockResolvedValue({
      kind: "ok",
      data: [{ conceptId: "skill_react", label: "React" }],
    });
    expect(await suggestSkillsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "candidates",
      candidates: [{ conceptId: "skill_react", label: "React" }],
    });
  });

  it("övrigt fel → error", async () => {
    getParsedResumeSkillsMock.mockResolvedValue({ kind: "error" });
    expect(await suggestSkillsFromParsedResumeAction(VALID_ID)).toEqual({
      kind: "error",
    });
  });
});

describe("updateMatchPreferencesAction (#1918, ADR 0147)", () => {
  const ENDPOINT = "/api/v1/me/match-preferences";

  beforeEach(() => {
    revalidatePathMock.mockReset();
    getSessionIdMock.mockReset().mockResolvedValue("sess-current");
    authedFetchMock.mockReset().mockResolvedValue(new Response(null, { status: 204 }));
  });

  function sentBody(): unknown {
    const init = authedFetchMock.mock.calls[0]?.[2] as RequestInit | undefined;
    return JSON.parse(String(init?.body));
  }

  it("PATCHes exactly the part it is given and revalidates both pages", async () => {
    const result = await updateMatchPreferencesAction({ skills: { preferredSkills: ["sk_a"] } });

    expect(result).toEqual({ success: true });
    expect(authedFetchMock).toHaveBeenCalledWith("sess-current", ENDPOINT, {
      method: "PATCH",
      body: JSON.stringify({ skills: { preferredSkills: ["sk_a"] } }),
    });
    expect(revalidatePathMock).toHaveBeenCalledWith("/mina-sidor");
    expect(revalidatePathMock).toHaveBeenCalledWith("/oversikt");
  });

  it("sends occupations without the years key when the caller leaves it out", async () => {
    await updateMatchPreferencesAction({ occupations: { preferredOccupationGroups: ["grp_a"] } });

    expect(sentBody()).toEqual({ occupations: { preferredOccupationGroups: ["grp_a"] } });
  });

  it("sends the rail's four parts and no experience", async () => {
    await updateMatchPreferencesAction({
      occupations: {
        preferredOccupationGroups: ["grp_a"],
        preferredOccupationExperience: [{ conceptId: "grp_a", years: 3 }],
      },
      skills: { preferredSkills: [] },
      locations: { preferredRegions: [], preferredMunicipalities: [], preferredRemote: true },
      employmentTypes: { preferredEmploymentTypes: [] },
    });

    expect(Object.keys(sentBody() as object)).toEqual([
      "occupations",
      "skills",
      "locations",
      "employmentTypes",
    ]);
  });

  it.each([
    // m8: ADR 0146's replay cap, in the user's words rather than the generic state conflict.
    [409, "En annan ändring sparades samtidigt. Ladda om sidan och försök igen."],
    [429, "För många försök. Vänta en stund och försök igen."],
    [500, "Ändringen kunde inte sparas. Försök igen om en stund."],
  ])("maps %i to its copy and revalidates nothing", async (status, error) => {
    authedFetchMock.mockResolvedValue(new Response(null, { status }));

    expect(await updateMatchPreferencesAction({ experience: { experienceYears: 2 } })).toEqual({
      success: false,
      error,
    });
    expect(revalidatePathMock).not.toHaveBeenCalled();
  });

  it("answers a lost connection in its own copy", async () => {
    authedFetchMock.mockRejectedValue(new TypeError("fetch failed"));

    expect(await updateMatchPreferencesAction({ experience: { experienceYears: 2 } })).toEqual({
      success: false,
      error: "Kunde inte nå servern. Kontrollera din nätverksanslutning.",
    });
  });

  it("sends nothing without a session", async () => {
    getSessionIdMock.mockResolvedValue(null);

    expect(await updateMatchPreferencesAction({ skills: { preferredSkills: [] } })).toEqual({
      success: false,
      error: "Du är inte inloggad.",
    });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });

  it.each([
    ["a body with no part", {}],
    [
      "the flat body of the full PUT",
      {
        preferredOccupationGroups: [],
        preferredRegions: [],
        preferredMunicipalities: [],
        preferredRemote: false,
        preferredEmploymentTypes: [],
        preferredSkills: [],
      },
    ],
  ])("refuses %s in its own copy and sends nothing", async (_row, body) => {
    // A Server Action is a public endpoint, so a tab on an earlier build can post any body.
    const result = await updateMatchPreferencesAction(body as unknown as UpdateInput);

    expect(result).toEqual({ success: false, error: "Ogiltiga uppgifter." });
    expect(authedFetchMock).not.toHaveBeenCalled();
  });
});
