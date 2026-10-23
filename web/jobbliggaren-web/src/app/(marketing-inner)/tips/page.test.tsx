import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svTips from "../../../../messages/sv/content-tips.json";
import svLanding from "../../../../messages/sv/landing.json";
import { isProtectedPath } from "@/lib/auth/protected-routes";
import TipsPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-tips" | "landing") =>
    createTranslator({
      locale: "sv",
      messages: { "content-tips": svTips, landing: svLanding },
      namespace,
    }),
}));

async function renderPage() {
  const element = await TipsPage();
  return render(element);
}

describe("/tips page (#261)", () => {
  it("renderar en h1 och sektionerna från katalogen", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Tips för jobbsökande" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Anpassa ditt CV" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", {
        level: 2,
        name: "Håll koll på sista ansökningsdag",
      })
    ).toBeInTheDocument();
  });

  it("länkar till den publika katalogen och till CV-granskningen (#2178)", async () => {
    await renderPage();

    expect(screen.getByRole("link", { name: "Lediga jobb" })).toHaveAttribute(
      "href",
      "/lediga-jobb"
    );
    expect(
      screen.getByRole("link", { name: "Så granskar vi ditt CV" })
    ).toHaveAttribute("href", "/cv-granskning");
  });

  // An anonymous visitor following a link into the (app) group lands on the sign-in wall (#2178).
  it("länkar aldrig in i en inloggad vy", async () => {
    await renderPage();

    const hrefs = screen.getAllByRole("link").map((link) => link.getAttribute("href") ?? "");
    expect(hrefs.length).toBeGreaterThan(0);
    expect(hrefs.filter(isProtectedPath)).toEqual([]);
  });
});
