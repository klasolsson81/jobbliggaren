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
// Prop-capturing rather than `() => null`: a null mock never inspects props, so pointing
// this surface back at the generated-render path would pass the whole suite (test-writer,
// PR #1684).
const cvPreviewProps = vi.fn();
vi.mock("@/components/resumes/cv-preview", () => ({
  CvPreview: (props: Record<string, unknown>) => {
    cvPreviewProps(props);
    return null;
  },
}));
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

describe("/cv/granska/[parsedId] — the original file reaches CvPreview", () => {
  it("passes the PARSED original path and no atsTextUrl", async () => {
    cvPreviewProps.mockClear();
    getParsedResume.mockResolvedValue({ kind: "ok", data: detail("IncompleteContent") });

    render(await invoke());

    expect(cvPreviewProps).toHaveBeenCalledTimes(1);
    const props = cvPreviewProps.mock.calls[0]![0] as Record<string, unknown>;
    // Klas-direktiv 2026-09-06: the staging surface shows the user's OWN file. `/preview`
    // is our generated rendering and is exactly what had to go.
    expect(props.originalUrl).toBe(`/api/cv/parsed/${PARSED_ID}/original`);
    expect(String(props.originalUrl)).not.toContain("/preview");
    // No canonical id here, so no ATS-text tab — and therefore no tab row at all.
    expect(props.atsTextUrl).toBeUndefined();
    // The file name the page already renders, so the download is identifiable.
    expect(props.fileName).toBe("cv.pdf");
  });
});

describe("/cv/granska/[parsedId] — the Beta marker", () => {
  it("marks the staging review Beta, because it is the one a user meets FIRST", async () => {
    getParsedResume.mockResolvedValue({ kind: "ok", data: detail("IncompleteContent") });

    const { container } = render(await invoke());

    // The import flow lands here BEFORE promotion, so an unmarked staging surface would
    // implicitly claim it is not beta while the canonical one says it is — inverting the
    // marker's purpose (design-reviewer, PR #1684).
    const kicker = container.querySelector(".jp-pagehero__kicker");
    expect(kicker).not.toBeNull();
    expect(kicker!.textContent).toBe("Beta");
    // The reservation itself is the sentence, and it lives in the lede so the skeleton
    // reserves the right band height without a second paragraph.
    expect(screen.getByText(/Granskningen är ny och byggs vidare/)).toBeInTheDocument();
  });
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
