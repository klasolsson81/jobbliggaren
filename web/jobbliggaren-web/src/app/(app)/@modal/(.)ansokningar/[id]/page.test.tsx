import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator, createFormatter } from "next-intl";
import svPages from "../../../../../../messages/sv/pages.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  AdSnapshotDto,
  ApplicationDetailDto,
} from "@/lib/dto/applications";
import InterceptedAnsokanModal from "./page";

const redirect = vi.fn();
const notFound = vi.fn(() => {
  throw new Error("NEXT_NOT_FOUND");
});
const getServerSession = vi.fn();
const getApplicationById =
  vi.fn<() => Promise<ApiResult<ApplicationDetailDto>>>();

// The async server page resolves copy via `getTranslations(...)` and dates via
// `getFormatter()` from next-intl/server (unavailable in jsdom). Mock both to
// real next-intl instances over the Swedish catalogs (source of truth) so the
// modal header renders the actual strings — and the dialog's accessible name
// (the <h2>) is asserted against the real copy.
vi.mock("next-intl/server", () => ({
  // The page only calls getTranslations("pages"); the client islands
  // (ApplicationModalShell, NotesSection) resolve their own copy via the test
  // render's NextIntlClientProvider (full sv catalog), not this server entry.
  getTranslations: async (namespace?: "pages") =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace,
    }),
  getFormatter: async () =>
    createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/applications", () => ({
  getApplicationById: () => getApplicationById(),
}));

// Partial mock: keep the real module but override the server-control-flow
// helpers the page calls AND useRouter (the client ApplicationModalShell calls
// it; the real hook needs an AppRouterContext provider absent in jsdom — stub
// router.back() since closing is not exercised here).
vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({ back: vi.fn(), push: vi.fn(), replace: vi.fn() }),
    redirect: (url: string) => {
      redirect(url);
      throw new Error(`NEXT_REDIRECT:${url}`);
    },
    notFound: () => notFound(),
  };
});

// Server-actions consumed by the client islands inside ApplicationDetail's tree
// (StatusEditCard/AddNoteForm/AddFollowUpForm) — same mock as the component test.
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: vi.fn().mockResolvedValue({ success: true }),
  addNoteAction: vi.fn().mockResolvedValue({ success: true }),
  addFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
  recordFollowUpOutcomeAction: vi.fn().mockResolvedValue({ success: true }),
}));

function makeDetail(
  overrides: Partial<ApplicationDetailDto> = {}
): ApplicationDetailDto {
  return {
    id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01T08:00:00Z",
    updatedAt: "2026-05-10T08:00:00Z",
    jobAd: {
      jobAdId: "ad-1",
      title: "Backend-utvecklare",
      company: "Volvo",
      url: "https://example.com/ad",
      source: "Platsbanken",
      publishedAt: "2026-05-01",
      expiresAt: "2026-06-01",
    },
    coverLetter: null,
    followUps: [],
    notes: [],
    ...overrides,
  };
}

function makeSnapshot(
  overrides: Partial<AdSnapshotDto> = {}
): AdSnapshotDto {
  return {
    title: "Systemutvecklare .NET",
    company: "Spotify",
    location: "Stockholm",
    url: "https://example.com/saved-ad",
    source: "Platsbanken",
    publishedAt: "2026-04-10T08:00:00Z",
    expiresAt: "2026-05-10T08:00:00Z",
    description: "Vi söker en utvecklare.",
    capturedAt: "2026-04-12T08:00:00Z",
    ...overrides,
  };
}

async function renderModal() {
  const element = await InterceptedAnsokanModal({
    params: Promise.resolve({ id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }),
  });
  return render(element);
}

describe("@modal/(.)ansokningar/[id] page header (#315 / ADR 0086)", () => {
  beforeEach(() => {
    redirect.mockReset();
    getServerSession.mockReset();
    getApplicationById.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
  });

  it("live-annons → modalrubriken (dialogens namn) = jobAd.title", async () => {
    getApplicationById.mockResolvedValue({ kind: "ok", data: makeDetail() });
    await renderModal();
    // Dialogens tillgängliga namn = <h2 id=labelId> via aria-labelledby.
    expect(
      screen.getByRole("dialog", { name: /Backend-utvecklare/ })
    ).toBeInTheDocument();
  });

  it("live-annons borta + snapshot → modalrubriken = BEVARAD titel, EJ mono-#id", async () => {
    getApplicationById.mockResolvedValue({
      kind: "ok",
      data: makeDetail({ jobAd: null, preservedAd: makeSnapshot() }),
    });
    await renderModal();

    // KÄRNAN i fyndet: dialogens accessible name MÅSTE vara den bevarade titeln
    // (samma som kroppens "Om annonsen (sparad kopia)"-panel), inte mono-#id.
    const dialog = screen.getByRole("dialog", {
      name: /Systemutvecklare \.NET/,
    });
    expect(dialog).toBeInTheDocument();
    // Headerns <h2> bär den bevarade titeln och är INTE mono (riktig prosa).
    const heading = screen.getByRole("heading", {
      name: "Systemutvecklare .NET",
    });
    expect(heading).not.toHaveClass("jp-mono");
    // Den generiska mono-#id-fallbacken får INTE visas.
    expect(
      screen.queryByText("Ansökan #aaaaaaaa")
    ).not.toBeInTheDocument();
    // Subtitle bär "sparad kopia"-markören (företag + markör).
    expect(
      screen.getByText(/Spotify · sparad kopia/)
    ).toBeInTheDocument();
  });

  it("varken annons eller snapshot → oförändrad mono-#id-fallback", async () => {
    getApplicationById.mockResolvedValue({
      kind: "ok",
      data: makeDetail({ jobAd: null, jobAdId: null, preservedAd: null }),
    });
    await renderModal();

    const heading = screen.getByRole("heading", { name: "Ansökan #aaaaaaaa" });
    expect(heading).toHaveClass("jp-mono");
  });
});
