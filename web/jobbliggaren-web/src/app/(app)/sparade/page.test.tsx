import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import svPages from "../../../../messages/sv/pages.json";
import type { SavedJobAdDto } from "@/lib/dto/saved-job-ads";
import SparadePage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace: namespace as "pages" | undefined,
    }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: async () => ({ email: "a@b.se", roles: [] }),
}));

const SAVED: SavedJobAdDto = {
  id: "s1",
  jobAdId: "j1",
  savedAt: "2026-05-23T15:00:00Z",
  jobAd: {
    jobAdId: "j1",
    title: "Backendutvecklare",
    company: "Acme AB",
    url: null,
    source: "Platsbanken",
    publishedAt: "2026-05-20T08:00:00Z",
    expiresAt: "2026-06-20T08:00:00Z",
  },
};

vi.mock("@/lib/api/saved-job-ads", () => ({
  getSavedJobAds: async () => ({ kind: "ok", data: [SAVED] }),
}));

vi.mock("@/lib/actions/saved-job-ads", () => ({
  unsaveJobAdAction: async () => ({ success: true }),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
  useRouter: () => ({ push: vi.fn() }),
}));

// #2029: the list's last fallback is this page's h1, reached by the id the page hands it.
describe("/sparade — the h1 a removal falls back to", () => {
  it("takes focus when the last saved ad is removed", async () => {
    render(await SparadePage());
    const h1 = screen.getByRole("heading", { level: 1 });

    screen.getByRole("button", { name: "Ta bort bokmärke för Backendutvecklare" }).focus();
    await userEvent.setup().keyboard("{Enter}");

    expect(h1).toHaveFocus();
  });
});
