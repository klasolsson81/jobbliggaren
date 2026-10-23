import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svLegal from "../../../../messages/sv/content-legal.json";
import svMetadata from "../../../../messages/sv/metadata.json";
import OmPage, { generateMetadata } from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "content-legal") =>
    createTranslator({
      locale: "sv",
      messages: { "content-legal": svLegal },
      namespace,
    }),
}));

async function renderPage() {
  const element = await OmPage();
  return render(element);
}

describe("/om page (#262)", () => {
  it("renderar h1 och sektionerna ur katalogen", async () => {
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Om Jobbliggaren" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Varför Jobbliggaren finns" })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Vad vi står för" })
    ).toBeInTheDocument();
  });

  // #2178: the template would render "Om Jobbliggaren | Jobbliggaren", a repeated site name.
  it("names the site once in its title, outside the root's title template", async () => {
    const { title } = await generateMetadata();

    expect(title).toEqual({ absolute: "Om Jobbliggaren" });
    expect(svMetadata.titleTemplate.replace("%s", "")).toContain(svMetadata.applicationName);
  });

  it("länkar till Klas projekt och till /kontakt", async () => {
    await renderPage();

    expect(
      screen.getByRole("link", { name: "klasolsson.se" })
    ).toHaveAttribute("href", "https://klasolsson.se");
    expect(
      screen.getByRole("link", { name: "kalaskoll.se" })
    ).toHaveAttribute("href", "https://kalaskoll.se");
    expect(
      screen.getByRole("link", { name: "Kontakta oss" })
    ).toHaveAttribute("href", "/kontakt");
  });
});
