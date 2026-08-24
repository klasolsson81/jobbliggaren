import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import svMessages from "../../../messages/sv";
import AuthLayout from "./layout";

// next/link and <LanguageSwitcher/> resolve navigation hooks; stub the navigation
// surface so the chrome renders in jsdom (mirrors site-header.test.tsx).
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/logga-in",
  useSearchParams: () => new URLSearchParams(),
}));

// Async Server Component. getMessages returns the REAL catalog so the layout's
// pickClientMessages() call runs for real rather than against a stub.
vi.mock("next-intl/server", () => ({
  getLocale: async () => "sv",
  getMessages: async () => svMessages,
  getTranslations: async () => (key: string) => key,
}));

describe("(auth)/layout — the collection surface's route to the cookie policy", () => {
  it("renders the footer, and the footer reaches /cookies", async () => {
    // security-auditor Minor 1 on PR #1493. Since that PR the 180-day retention
    // statement lives ONLY on /cookies, so Art. 13(2)(a) is satisfied by the
    // layered-notice route rather than by a hint beside the control. WP260 rev.01
    // requires the further layer to be directly accessible from the surface where
    // the data is collected — that is this footer link, and nothing pinned it.
    // Bites on revert: dropping SiteFooter from this layout drops the disclosure
    // silently, which the auditor graded as Major if it happened.
    //
    // Queried structurally, not by accessible name: SiteFooter is a Server
    // Component whose useTranslations("landing") resolves from the request catalog
    // in production, while this layout's own provider carries a narrower set —
    // jsdom has no server pass, so the labels come back as keys here. The link's
    // href is the property that matters anyway.
    const { container } = render(await AuthLayout({ children: null }));

    const footer = screen.getByRole("contentinfo");
    expect(footer).toBeInTheDocument();
    expect(container.querySelector('a[href="/cookies"]')).not.toBeNull();
  });
});
