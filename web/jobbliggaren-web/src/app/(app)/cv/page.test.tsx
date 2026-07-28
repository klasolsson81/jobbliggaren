import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../messages/sv/pages.json";
import type { PendingParsedResumeResult } from "@/lib/api/resumes";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { GetResumesResult } from "@/lib/dto/resumes";
import CvListPage from "./page";

type ResumeListResult = ApiResult<GetResumesResult>;

/**
 * /cv — hubben (#1060 PR C).
 *
 * Vad som pinnas: åtgärdskortet och tomt-tillståndet får ALDRIG renderas samtidigt.
 * De var oberoende conditionals, så en användare med ett inläst men osparat CV fick
 * "Kräver åtgärd" direkt ovanför "Inga CV ännu" — två motsägande besked om samma fil,
 * och exakt det Klas rapporterade i #1060.
 */

const redirect = vi.fn();
const getServerSession = vi.fn();
const getResumes = vi.fn<() => Promise<ResumeListResult>>();
const getLatestPendingParsedResume = vi.fn<() => Promise<PendingParsedResumeResult>>();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace: namespace as "pages" | undefined,
    }),
  getFormatter: async () => ({
    dateTime: (value: Date) =>
      new Intl.DateTimeFormat("sv-SE", {
        hour: "2-digit",
        minute: "2-digit",
      }).format(value),
  }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/resumes", () => ({
  getResumes: () => getResumes(),
  getLatestPendingParsedResume: () => getLatestPendingParsedResume(),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    redirect(url);
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

// The discard control is a client island with its own dialog; the hub test is about
// which blocks render, not about that interaction (it has its own suite).
vi.mock("@/components/resumes/discard-draft-button", () => ({
  DiscardDraftButton: () => null,
}));

const PENDING = {
  id: "11111111-1111-4111-8111-111111111111",
  sourceFileName: "cv.pdf",
  uploadedAt: "2026-07-28T09:00:00Z",
  gaps: null,
};

beforeEach(() => {
  redirect.mockClear();
  getServerSession.mockReset();
  getResumes.mockReset();
  getLatestPendingParsedResume.mockReset();
  getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
});

// Shaped, not cast: a cast would have hidden that GetResumesResult carries paging fields, and
// a fixture that does not typecheck against the real contract is a fixture that can drift from
// it silently.
function emptyList(): ResumeListResult {
  return {
    kind: "ok",
    data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
  };
}

describe("/cv — the pending card and the empty state are mutually exclusive", () => {
  it("shows the action card and SUPPRESSES the empty state when a pending artifact exists", async () => {
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: PENDING });

    render(await CvListPage());

    // The truthful block: the file exists, it is read, it is not saved.
    expect(screen.getByText("Ditt CV är inläst")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Öppna granskningen/ })).toHaveAttribute(
      "href",
      `/cv/granska/${PENDING.id}`,
    );

    // The contradiction that must be gone. "Inga CV ännu" next to "Kräver åtgärd" told the
    // user two different things about one file.
    expect(screen.queryByText("Inga CV ännu")).not.toBeInTheDocument();
    expect(screen.queryByText("Skapa första CV")).not.toBeInTheDocument();
  });

  it("still shows the empty state when the list is empty AND nothing is pending", async () => {
    // The suppression must be conditional on the pending artifact, not on the list being
    // empty — otherwise the fix would delete a legitimate empty state for every new user.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: null });

    render(await CvListPage());

    expect(screen.getByText("Inga CV ännu")).toBeInTheDocument();
    expect(screen.queryByText("Ditt CV är inläst")).not.toBeInTheDocument();
  });

  it("still shows the empty state when the pending fetch FAILED (degrades civilly)", async () => {
    // A non-ok pending result means "we do not know", and the page already treats that as
    // "no card". The empty state must come back with it, or a transient backend blip would
    // render a hub with no content and no explanation at all.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "error" });

    render(await CvListPage());

    expect(screen.getByText("Inga CV ännu")).toBeInTheDocument();
  });

  it("routes to the review, and no longer promises a re-upload is the only way forward", async () => {
    // The old body ended "…innan du laddar upp den igen", which presumes the fix is always in
    // the file. It is not (a personnummer in the CV NAME is fixed at the name field), and it
    // was the copy that made the reason unknowable without a second upload.
    getResumes.mockResolvedValue(emptyList());
    getLatestPendingParsedResume.mockResolvedValue({ kind: "ok", data: PENDING });

    render(await CvListPage());

    expect(
      screen.getByText(/Öppna granskningen så ser du vad som saknas/),
    ).toBeInTheDocument();
    expect(screen.queryByText(/innan du laddar upp den igen/)).not.toBeInTheDocument();
  });
});
