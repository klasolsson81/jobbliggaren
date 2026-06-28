import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import KontaktPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await KontaktPage();
  return render(element);
}

describe("/kontakt page (#262)", () => {
  it("renderar h1 och e-postlänk (mailto)", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Kontakta oss" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "klasolsson81@gmail.com" })
    ).toHaveAttribute("href", "mailto:klasolsson81@gmail.com");
  });
});
