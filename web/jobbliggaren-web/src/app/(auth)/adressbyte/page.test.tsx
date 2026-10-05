import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../messages/sv/pages.json";
import sitemap from "@/app/sitemap";

// #1975 — /adressbyte as a document (design-reviewer item 9): its title and heading say what it is for and carry no
// address, it asks not to be indexed and is not in the sitemap, and it is rendered per request.

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { [namespace]: svPages }, namespace }),
}));
vi.mock("@/lib/auth/address-change-actions", () => ({ completeAddressChange: vi.fn() }));

import AddressChangePage, { dynamic, generateMetadata } from "./page";

describe("/adressbyte (#1975, ADR 0153)", () => {
  it("is titled by what it is for, with no address, and asks not to be indexed", async () => {
    expect(await generateMetadata()).toEqual({
      title: "Bekräfta ny e-postadress",
      robots: { index: false, follow: false },
    });
  });

  it("is headed by the same words and holds the form, with nothing above it", async () => {
    render(await AddressChangePage());

    const heading = screen.getByRole("heading", { level: 1, name: "Bekräfta ny e-postadress" });
    expect(heading.nextElementSibling?.tagName).toBe("FORM");
    expect(screen.getByRole("button", { name: "Byt adress" })).toBeInTheDocument();
  });

  it("is rendered per request and never prerendered", () => {
    expect(dynamic).toBe("force-dynamic");
  });

  it("is not in the sitemap", () => {
    expect(sitemap().map((entry) => new URL(entry.url).pathname)).not.toContain("/adressbyte");
  });
});
