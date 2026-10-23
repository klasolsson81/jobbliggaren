import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svTips from "../../../../messages/sv/content-tips.json";
import svLanding from "../../../../messages/sv/landing.json";
import TipsPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-tips" | "landing") =>
    createTranslator({
      locale: "sv",
      messages: { "content-tips": svTips, landing: svLanding },
      namespace,
    }),
}));

vi.mock("next/link", () => ({
  default: ({ href, prefetch, children, ...rest }: { href: string; prefetch?: boolean | null; children: ReactNode }) => (
    <a href={href} data-prefetch={prefetch === undefined ? "default" : String(prefetch)} {...rest}>
      {children}
    </a>
  ),
}));

describe("/tips prefetch (ADR 0176 F10, F15)", () => {
  it("links the catalogue with a plain anchor, a document load that never prefetches", async () => {
    render(await TipsPage());

    const catalogue = screen.getByRole("link", { name: "Lediga jobb" });
    expect(catalogue).toHaveAttribute("href", "/lediga-jobb");
    expect(catalogue).not.toHaveAttribute("data-prefetch");
  });
});
