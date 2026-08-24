import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svFallback from "../../../../messages/sv/fallback.json";
import GuestNotFound from "./not-found";

// Async Server Component using getTranslations; mock it to a real Swedish
// translator so the rendered copy is the shipped copy (mirrors the pattern in
// (auth)/bekrafta-konto/page.test.tsx).
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({
      locale: "sv",
      messages: { [namespace]: svFallback },
      namespace,
    }),
}));

describe("(guest)/gast/not-found boundary (#1477)", () => {
  it("renders the 404 copy and a way back INTO guest mode", async () => {
    render(await GuestNotFound());

    expect(
      screen.getByRole("heading", { name: "Sidan finns inte" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Adressen kan vara felstavad eller så har sidan tagits bort.",
      ),
    ).toBeInTheDocument();
    // Without this file the four guest notFound() call sites fell through to
    // the ROOT not-found, which renders the public marketing frame — the wrong
    // shell for a visitor who is inside guest mode.
    expect(
      screen.getByRole("link", { name: "Till översikten" }),
    ).toHaveAttribute("href", "/gast/oversikt");
  });
});
