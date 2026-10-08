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

  it.each(["sv", "en"] as const)("publishes the grace period, unavailable restore and separate backup boundary (%s)", async (language) => {
    locale = language;
    render(await IntegritetPage());

    if (language === "sv") {
      expect(screen.getByText(/När du begär radering av ditt konto spärras åtkomsten direkt/))
        .toHaveTextContent("Du kan inte återställa kontot eller avbryta raderingen.");
      expect(screen.getByText(/När den permanenta raderingskörningen rensar ditt konto/))
        .toHaveTextContent("Kontoraderingen tar inte bort säkerhetskopior omedelbart.");
      expect(screen.queryByText(/ett konto kan återställas|även från en eventuell säkerhetskopia/)).not.toBeInTheDocument();
    } else {
      expect(screen.getByText(/When you request deletion of your account, access is blocked immediately/))
        .toHaveTextContent("You cannot restore the account or cancel deletion.");
      expect(screen.getByText(/When the permanent deletion job cleans up your account/))
        .toHaveTextContent("Account deletion does not remove backups immediately.");
      expect(screen.queryByText(/an account can be restored|even from a possible backup/)).not.toBeInTheDocument();
    }
  });
});
