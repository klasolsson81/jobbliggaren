import { describe, it, expect, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svAdmin from "../../../../../messages/sv/admin.json";
import AdminFeedbackPage from "./page";

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({ locale: "sv", messages: { admin: svAdmin }, namespace: namespace as "admin" }),
}));

async function renderPage() {
  render(await AdminFeedbackPage());
}

function describedText(element: HTMLElement): string | undefined {
  const describedBy = element.getAttribute("aria-describedby") ?? "";
  return document.getElementById(describedBy)?.textContent ?? undefined;
}

describe("/admin/feedback — the master/detail layout before #1979 (ADR 0150 D2)", () => {
  it("is headed Feedback and says what the page holds", async () => {
    await renderPage();

    expect(screen.getByRole("heading", { level: 1, name: "Feedback" })).toBeInTheDocument();
    expect(screen.getByText("Rapporter från appens feedback-knapp.")).toBeInTheDocument();
  });

  it("shows the status filters without counts, disabled", async () => {
    await renderPage();

    const group = screen.getByRole("group", { name: "Visa rapporter" });
    const buttons = within(group).getAllByRole("button");
    expect(buttons.map((button) => button.textContent)).toEqual([
      "Alla",
      "Nya",
      "Pågår",
      "Lösta",
      "Skippade",
    ]);
    for (const button of buttons) {
      expect(button).toBeDisabled();
      expect(describedText(button)).toBe("Kommer snart");
    }
  });

  it("holds no report in the list, only its Kommer snart line", async () => {
    await renderPage();

    const list = screen.getByRole("region", { name: "Rapporter" });
    expect(list.textContent).toBe("RapporterKommer snart");
  });

  it("keeps the reply form, disabled and described, and never as a solid primary action", async () => {
    await renderPage();

    const detail = screen.getByRole("region", { name: "Vald rapport" });
    const reply = within(detail).getByRole("textbox", { name: "Svar" });
    expect(reply).toBeDisabled();
    expect(describedText(reply)).toBe("Kommer snart");

    const send = within(detail).getByRole("button", { name: "Skicka svar" });
    expect(send).toBeDisabled();
    expect(describedText(send)).toBe("Kommer snart");
    expect(send).not.toHaveClass("jp-btn--primary");
  });
});
