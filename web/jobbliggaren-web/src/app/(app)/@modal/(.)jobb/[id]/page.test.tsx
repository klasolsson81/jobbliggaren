import { describe, it, expect, vi, beforeEach } from "vitest";
import userEvent from "@testing-library/user-event";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../messages/sv/pages.json";
import svFallback from "../../../../../../messages/sv/fallback.json";
import type { JobDetailLoad } from "@/lib/job-ads/load-job-detail-data";
import InterceptedJobbModal from "./page";

const back = vi.fn();
const redirect = vi.fn();
const notFound = vi.fn(() => {
  throw new Error("NEXT_NOT_FOUND");
});
const getServerSession = vi.fn();
const loadJobDetailData = vi.fn<() => Promise<JobDetailLoad>>();

// The page resolves its copy through `getTranslations` from next-intl/server (unavailable in jsdom), so the mock
// is a real translator over the Swedish catalogs the page reads; the client shell resolves its own copy through
// the test render's provider.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: "pages" | "fallback") =>
    createTranslator({ locale: "sv", messages: { pages: svPages, fallback: svFallback }, namespace }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
  getSessionId: async () => "session-id",
}));

vi.mock("@/lib/job-ads/load-job-detail-data", () => ({
  loadJobDetailData: () => loadJobDetailData(),
}));

vi.mock("@/lib/api/company-follows", () => ({
  markFollowedCompanyAdSeen: vi.fn(),
}));

vi.mock("next/server", () => ({
  after: vi.fn(),
}));

vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({ back, push: vi.fn(), replace: vi.fn() }),
    redirect: (url: string) => {
      redirect(url);
      throw new Error(`NEXT_REDIRECT:${url}`);
    },
    notFound: () => notFound(),
  };
});

const pages = createTranslator({ locale: "sv", messages: { pages: svPages }, namespace: "pages" });

async function renderModal() {
  const element = await InterceptedJobbModal({
    params: Promise.resolve({ id: "19630000-0000-4000-8000-000000000103" }),
    searchParams: Promise.resolve({}),
  });
  return render(element);
}

async function expectCloseFooter(dialog: HTMLElement) {
  expect(dialog).toHaveClass("jp-modal--message");
  const close = within(dialog).getByText("Stäng", { selector: "button" });
  expect(close).toHaveClass("jp-btn", "jp-btn--secondary");
  expect(close.closest(".jp-modal__foot")).toHaveTextContent(/^Stäng$/);
  expect(dialog.querySelectorAll(".jp-modal__foot")).toHaveLength(1);
  await userEvent.click(close);
  expect(back).toHaveBeenCalledTimes(1);
}

describe("@modal/(.)jobb/[id] — an outcome without an ad, in the job modal's own sheet", () => {
  beforeEach(() => {
    back.mockReset();
    redirect.mockReset();
    notFound.mockClear();
    getServerSession.mockReset();
    loadJobDetailData.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
  });

  it.each([
    [{ kind: "rateLimited", retryAfterSeconds: 30 } as const, pages("common.rateLimitedTitle"), pages("common.rateLimitedBody", { seconds: 30 })],
    [{ kind: "error" } as const, svPages.jobb.detail.loadErrorTitle, svFallback.errorBodyRetry],
  ])("$kind renders its message in the sheet as the dialog's description", async (result, title, body) => {
    loadJobDetailData.mockResolvedValue(result);
    await renderModal();

    const dialog = screen.getByRole("dialog", { name: title });
    expect(dialog).toHaveAttribute("aria-describedby", "jp-modal-desc");
    expect(document.getElementById("jp-modal-desc")).toHaveTextContent(body);
    expect(dialog).toHaveClass("jp-modal--sheet");
    await expectCloseFooter(dialog);
  });

  // #1987: a notFound() thrown here escapes the slot to the root boundary, which swaps the signed-in shell for the
  // public frame. 404 and 410 both arrive as `notFound`, and both read the full page's copy.
  it("an ad that is gone renders 'Sidan finns inte' in the sheet and never calls notFound()", async () => {
    loadJobDetailData.mockResolvedValue({ kind: "notFound" });
    await renderModal();

    const dialog = screen.getByRole("dialog", { name: svFallback.notFound.title });
    expect(dialog).toHaveAttribute("aria-describedby", "jp-modal-desc");
    expect(document.getElementById("jp-modal-desc")).toHaveTextContent(svFallback.notFound.body);
    expect(dialog).toHaveClass("jp-modal--sheet");
    await expectCloseFooter(dialog);
    expect(notFound).not.toHaveBeenCalled();
  });
});
