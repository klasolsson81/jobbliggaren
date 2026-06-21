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
// next/cache revalidatePath är en no-op i test.
vi.mock("next/cache", () => ({ revalidatePath: vi.fn() }));
// getSessionId används av updateMatchPreferencesAction (ej testad här) — stubba.
vi.mock("@/lib/auth/session", () => ({ getSessionId: vi.fn() }));

import {
  suggestOccupationsFromCvAction,
  suggestOccupationsFromParsedResumeAction,
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
