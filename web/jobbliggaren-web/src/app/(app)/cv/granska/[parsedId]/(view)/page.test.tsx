import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../../messages/sv/pages.json";
import type { ParsedResumeDetailDto } from "@/lib/dto/parsed-resume";
import CvReviewPage from "./page";

/**
 * /cv/granska/[parsedId] — the surface #1060 PR C exists to deliver.
 *
 * This file was missing, and `code-reviewer` found what that cost: deleting the
 * `<CvBlockReason reason={parsed.blockReason} />` line from the page survived the entire
 * suite. The component had its own tests and the DTO had its own tests, and the wiring
 * between them — DTO → page → component, i.e. the whole user-visible deliverable — had none.
 * That is the FE-survivor class the two previous PRs in this lane were both bitten by.
 */

const redirect = vi.fn();
const notFound = vi.fn();
const getServerSession = vi.fn();
const getParsedResume = vi.fn();
const getCvReview = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace: namespace as "pages" | undefined,
    }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/resumes", () => ({
  getParsedResume: (id: string) => getParsedResume(id),
  getCvReview: (id: string, profile: string) => getCvReview(id, profile),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    redirect(url);
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  notFound: () => {
    notFound();
    throw new Error("NEXT_NOT_FOUND");
  },
}));

// Client islands with their own suites; the page test is about which blocks render.
vi.mock("@/components/resumes/cv-preview", () => ({ CvPreview: () => null }));
vi.mock("@/components/resumes/cv-review-panel", () => ({ CvReviewPanel: () => null }));

const PARSED_ID = "11111111-1111-4111-8111-111111111111";

function detail(
  blockReason: ParsedResumeDetailDto["blockReason"],
): ParsedResumeDetailDto {
  return {
    id: PARSED_ID,
    status: "PendingReview",
    detectedLanguage: "Sv",
    sourceFileName: "cv.pdf",
    confidence: {
      overall: "Degraded",
      requiresManualReview: true,
      fallback: "None",
      sections: [],
    },
    personnummer: { found: false, count: 0, kinds: [] },
    content: {
      contact: { fullName: null, email: null, phone: null, location: null },
      profile: null,
      experiences: [],
      educations: [],
      skills: [],
      languages: [],
      sections: [],
      preamble: null,
    },
    occupationProposals: [],
    createdAt: "2026-07-28T09:00:00Z",
    updatedAt: "2026-07-28T09:00:00Z",
    blockReason,
  };
}

function invoke() {
  return CvReviewPage({
    params: Promise.resolve({ parsedId: PARSED_ID }),
    searchParams: Promise.resolve({}),
  });
}

beforeEach(() => {
  redirect.mockClear();
  notFound.mockClear();
  getServerSession.mockReset();
  getParsedResume.mockReset();
  getCvReview.mockReset();
  getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
  getCvReview.mockResolvedValue({ kind: "error" });
});

describe("/cv/granska/[parsedId] — the block reason reaches the page", () => {
  it("renders the reason the DTO carried, not a generic block", async () => {
    getParsedResume.mockResolvedValue({ kind: "ok", data: detail("IncompleteContent") });

    render(await invoke());

    expect(
      screen.getByRole("heading", { name: "Därför är filen inte sparad som CV" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/anställning har arbetsgivare och titel/i)).toBeInTheDocument();
  });

  it("carries the ACCOUNT-NAME reason through to its own copy and control", async () => {
    // The wiring that matters most: this reason renders on a page where every file-side
    // surface says "clean", so if the page passed the wrong value nothing else would betray it.
    getParsedResume.mockResolvedValue({
      kind: "ok",
      data: detail("PersonnummerInAccountName"),
    });

    render(await invoke());

    expect(screen.getByText(/Namnet på ditt konto innehåller ett personnummer/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Inställningar/ })).toBeInTheDocument();
  });

  it("renders the file-scoped cleared state when nothing blocks the artifact", async () => {
    getParsedResume.mockResolvedValue({ kind: "ok", data: detail(null) });

    render(await invoke());

    expect(
      screen.getByRole("heading", { name: "Inget i filen hindrar den" }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/uppfyller kraven/)).not.toBeInTheDocument();
  });

  it("still 404s and redirects on the existing dispositions", async () => {
    getParsedResume.mockResolvedValue({ kind: "notFound" });
    await expect(invoke()).rejects.toThrow("NEXT_NOT_FOUND");
    expect(notFound).toHaveBeenCalled();

    getParsedResume.mockResolvedValue({ kind: "unauthorized" });
    await expect(invoke()).rejects.toThrow("NEXT_REDIRECT:/logga-in");
  });
});
