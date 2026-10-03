import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import enLegal from "../../../../messages/en/content-legal.json";
import IntegritetPage from "./page";

let locale: "sv" | "en" = "sv";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale,
      messages: { "content-legal": locale === "sv" ? svLegal : enLegal },
      namespace,
    }),
}));

describe("/integritet published policy date (#1917)", () => {
  beforeEach(() => { locale = "sv"; });

  it.each(["sv", "en"] as const)("renders its published date visibly after h1 in the band (%s)", async (language) => {
    locale = language;
    const { container } = render(await IntegritetPage());
    const catalogue = language === "sv" ? svLegal : enLegal;
    const band = container.querySelector(".jp-pagehero__main");
    const date = screen.getByText(catalogue.privacy.updated);
    expect(band?.querySelectorAll("p.jp-pagehero__lede")).toHaveLength(1);
    expect(date.parentElement).toBe(band);
    expect(date.previousElementSibling?.tagName).toBe("H1");
    expect(date).toBeVisible();
    expect(date.closest('[aria-hidden="true"], .sr-only, details, [role="tooltip"]')).toBeNull();
    expect(screen.getAllByText(catalogue.privacy.updated)).toHaveLength(1);
  });
});
