import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svMessages from "../../messages/sv";
import RootNotFound from "./not-found";

// next/link and <LanguageSwitcher/> resolve navigation hooks; stub the
// navigation surface so the chrome renders in jsdom (mirrors site-header.test).
vi.mock("next/navigation", () => ({
  useRouter: () => ({ refresh: vi.fn(), push: vi.fn(), prefetch: vi.fn() }),
  usePathname: () => "/nagot-som-inte-finns",
  useSearchParams: () => new URLSearchParams(),
}));

// Async Server Component. getMessages returns the REAL catalog so the
// pickClientMessages() call under test runs for real rather than against a stub.
vi.mock("next-intl/server", () => ({
  getLocale: async () => "sv",
  getMessages: async () => svMessages,
  getTranslations: async (namespace: string) =>
    createTranslator({
      locale: "sv",
      messages: { [namespace]: svMessages.fallback },
      namespace,
    }),
}));

describe("root not-found — the last line for unmatched URLs (#1477)", () => {
  it("renders the 404 copy and a way back to the start page", async () => {
    render(await RootNotFound());

    expect(
      screen.getByRole("heading", { name: "Sidan finns inte", level: 1 }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Adressen kan vara felstavad eller så har sidan tagits bort.",
      ),
    ).toBeInTheDocument();

    const main = screen.getByRole("main");
    expect(
      within(main).getByRole("link", { name: "Till startsidan" }),
    ).toHaveAttribute("href", "/");
  });

  it("carries the public site frame — header, footer, skip-link target", async () => {
    // The defect this closes: a mistyped URL used to render bare, with no way
    // back except the browser's back button (Klas 2026-08-23).
    //
    // Queried structurally, not by accessible name. SiteHeader and SiteFooter
    // are Server Components: in production their `useTranslations("landing")`
    // resolves from the request catalog, and only the client-side
    // <LanguageSwitcher/> reads the provider this file seeds. jsdom has no
    // server pass, so it resolves the whole tree against that provider and the
    // `landing` labels come back as keys. Asserting them here would pin a
    // jsdom artefact; the payload itself is pinned by the test below and by
    // client-namespace-payload.test.ts.
    const { container } = render(await RootNotFound());

    const banner = screen.getByRole("banner");
    expect(banner.querySelector('a.jp-brand[href="/"]')).not.toBeNull();
    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
    expect(screen.getByRole("main")).toHaveAttribute("id", "main");
    expect(container.querySelector('a[href="#main"]')).not.toBeNull();
  });

  it("seeds its own client payload — root's must stay empty", async () => {
    // SiteHeader mounts the client-side <LanguageSwitcher/>, which reads
    // `common`. Charging that to the ROOT layout would put it in every document
    // in the app (ADR 0045 Beslut 6), so this file is its own i18n boundary and
    // the switcher must resolve from the provider it renders — a MISSING_MESSAGE
    // key would surface here as the raw key instead of the label.
    render(await RootNotFound());

    expect(screen.getByRole("button", { name: /Språk/ })).toBeInTheDocument();
  });
});
