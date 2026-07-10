import { describe, it, expect, vi, beforeEach } from "vitest";
import type { ResumeListItemDto } from "@/lib/dto/resumes";

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
// next/cache revalidatePath är en no-op i test.
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));
// getSessionId används av updateMatchPreferencesAction (ej testad här) — stubba.
vi.mock("@/lib/auth/session", () => ({ getSessionId: vi.fn() }));
// getTranslations används av searchSkillsAction:s fel-grenar — returnera nyckeln.
vi.mock("next-intl/server", () => ({
  getTranslations: vi.fn(async () => (key: string) => key),
}));

import {
  suggestOccupationsFromCvAction,
  suggestOccupationsFromParsedResumeAction,
  searchSkillsAction,
  suggestSkillsFromParsedResumeAction,
} from "./match-preferences";
import { pickPrimaryResume } from "@/components/settings/match-preferences-shared";

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
