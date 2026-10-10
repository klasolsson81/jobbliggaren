import { describe, it, expect, vi, beforeEach } from "vitest";
import { isValidElement } from "react";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../messages/sv/pages.json";
import type { CvReviewDto } from "@/lib/dto/parsed-resume";
import type { ResumeDetailDto } from "@/lib/dto/resumes";
import { CvPreamble } from "@/components/resumes/cv-preamble";
import CanonicalCvReviewPage from "./page";

/**
 * /cv/[id]/granska — the wiring between the two reads and the ledger (#2083). The panel and the
 * preamble have their own suites; this file pins what the page hands them, which no component test
 * can see: the CV's name in the identity row, the master version's preamble in the notice slot,
 * the canonical target that turns on the action column, and the civil degradation of a failed
 * review read.
 */

const redirect = vi.fn();
const notFound = vi.fn();
const getServerSession = vi.fn();
const getResumeById = vi.fn();
const getResumeReview = vi.fn();
const panelProps = vi.fn();

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
  getResumeById: (id: string) => getResumeById(id),
  getResumeReview: (id: string, profile: string) => getResumeReview(id, profile),
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

vi.mock("@/components/resumes/cv-review-panel", () => ({
  CvReviewPanel: (props: Record<string, unknown>) => {
    panelProps(props);
    return null;
  },
}));
vi.mock("@/components/feedback/page-feedback", () => ({ PageFeedback: () => null }));

const RESUME_ID = "22222222-2222-4222-8222-222222222222";

// Only the fields the page reads; the rest of the detail DTO is irrelevant here.
const RESUME = {
  id: RESUME_ID,
  name: "Importerat CV 2026-08-29",
  createdAt: "2026-08-29T09:00:00Z",
  updatedAt: "2026-08-29T09:00:00Z",
  versions: [
    {
      id: "v1",
      kind: "Master",
      content: { preamble: "Text ovanför rubriken" },
      createdAt: "2026-08-29T09:00:00Z",
      updatedAt: "2026-08-29T09:00:00Z",
    },
  ],
} as unknown as ResumeDetailDto;

const REVIEW = { rubricVersion: "2.3.0" } as unknown as CvReviewDto;

function invoke(searchParams: { profile?: string } = {}) {
  return CanonicalCvReviewPage({
    params: Promise.resolve({ id: RESUME_ID }),
    searchParams: Promise.resolve(searchParams),
  });
}

function lastPanelProps(): Record<string, unknown> {
  const calls = panelProps.mock.calls;
  return calls[calls.length - 1]![0] as Record<string, unknown>;
}

beforeEach(() => {
  redirect.mockClear();
  notFound.mockClear();
  panelProps.mockClear();
  getServerSession.mockReset();
  getResumeById.mockReset();
  getResumeReview.mockReset();
  getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
  getResumeById.mockResolvedValue({ kind: "ok", data: RESUME });
  getResumeReview.mockResolvedValue({ kind: "ok", data: REVIEW });
});

describe("/cv/[id]/granska — what the page hands the ledger", () => {
  it("the review, the canonical target, the CV's name and the master version's preamble", async () => {
    render(await invoke());

    const props = lastPanelProps();
    expect(props.review).toBe(REVIEW);
    expect(props.target).toEqual({ kind: "canonical", resumeId: RESUME_ID });
    expect(props.profile).toBe("Ats");
    expect(props.documentName).toBe("Importerat CV 2026-08-29");
    expect(isValidElement(props.notice)).toBe(true);
    const notice = props.notice as React.ReactElement<{ preamble: string | null }>;
    expect(notice.type).toBe(CvPreamble);
    expect(notice.props.preamble).toBe("Text ovanför rubriken");
  });

  it("reads the profile from the query and falls back to ATS for an unknown one", async () => {
    render(await invoke({ profile: "Visual" }));
    expect(getResumeReview).toHaveBeenLastCalledWith(RESUME_ID, "Visual");
    expect(lastPanelProps().profile).toBe("Visual");

    render(await invoke({ profile: "visual" }));
    expect(getResumeReview).toHaveBeenLastCalledWith(RESUME_ID, "Ats");
  });

  it("degrades a failed review read to null instead of failing the page", async () => {
    getResumeReview.mockResolvedValue({ kind: "error" });
    render(await invoke());
    expect(lastPanelProps().review).toBeNull();
  });
});

describe("/cv/[id]/granska — the resume read decides the page", () => {
  it("404s on a missing CV and sends a signed-out visitor to sign in", async () => {
    getResumeById.mockResolvedValue({ kind: "notFound" });
    await expect(invoke()).rejects.toThrow("NEXT_NOT_FOUND");

    getResumeById.mockResolvedValue({ kind: "unauthorized" });
    await expect(invoke()).rejects.toThrow("NEXT_REDIRECT:/logga-in");

    getServerSession.mockResolvedValue(null);
    await expect(invoke()).rejects.toThrow("NEXT_REDIRECT:/logga-in");
  });

  it("an error renders the civil error page and no ledger", async () => {
    getResumeById.mockResolvedValue({ kind: "error" });
    render(await invoke());
    expect(screen.getByRole("heading", { level: 1 })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Tillbaka till CV" })).toHaveAttribute("href", "/cv");
    expect(panelProps).not.toHaveBeenCalled();
  });
});
