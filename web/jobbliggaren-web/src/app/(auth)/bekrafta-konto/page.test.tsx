import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import type { ActionResult } from "@/lib/actions/_action-result";
import svPages from "../../../../messages/sv/pages.json";
import BekraftaKontoPage from "./page";

// #714 — the PUBLIC /bekrafta-konto registration-confirmation landing + its confirm island. Mirrors
// the #679 /bekrafta-epost test. The page is an async Server Component using getTranslations; mock it
// to a real Swedish translator so page copy matches the island copy. The confirm action is mocked so
// no fetch runs and the no-auto-POST invariant can be asserted (the action must fire only on click).

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({
      locale: "sv",
      messages: { [namespace]: svPages },
      namespace,
    }),
}));

const confirmAccountActionMock =
  vi.fn<(uid: string, token: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/confirm-account", () => ({
  confirmAccountAction: (uid: string, token: string) =>
    confirmAccountActionMock(uid, token),
}));

const UID = "0af1b2c3d4e5460788990011aabbccdd";
const TOKEN = "Q2ZERjQ4-token-base64url";

type SearchParams = Record<string, string | string[] | undefined>;

async function renderPage(searchParams: SearchParams) {
  const ui = await BekraftaKontoPage({
    searchParams: Promise.resolve(searchParams),
  });
  return render(ui);
}

describe("BekraftaKontoPage (#714 public registration confirm landing)", () => {
  beforeEach(() => {
    confirmAccountActionMock.mockReset();
    confirmAccountActionMock.mockResolvedValue({ success: true });
  });

  it("shows an invalid-link state and does NOT POST when params are missing", async () => {
    await renderPage({});

    expect(
      screen.getByRole("heading", { level: 1, name: "Länken går inte att använda" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Aktivera kontot" }),
    ).not.toBeInTheDocument();
    expect(confirmAccountActionMock).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute("href", "/logga-in");
  });

  it("treats a partial link (uid only, no token) as invalid", async () => {
    await renderPage({ uid: UID });

    expect(
      screen.getByRole("heading", { level: 1, name: "Länken går inte att använda" }),
    ).toBeInTheDocument();
    expect(confirmAccountActionMock).not.toHaveBeenCalled();
  });

  it("renders the confirm button but does NOT auto-POST on load when params are present", async () => {
    await renderPage({ uid: UID, token: TOKEN });

    expect(screen.getByRole("button", { name: "Aktivera kontot" })).toBeInTheDocument();
    // CRITICAL: the confirm POST must never fire on page load (mail scanners / prefetchers GET the link).
    expect(confirmAccountActionMock).not.toHaveBeenCalled();
  });

  it("fires the confirm action with the exact (uid, token) only on button click", async () => {
    const user = userEvent.setup();
    await renderPage({ uid: UID, token: TOKEN });

    await user.click(screen.getByRole("button", { name: "Aktivera kontot" }));

    await waitFor(() => expect(confirmAccountActionMock).toHaveBeenCalledWith(UID, TOKEN));
    expect(confirmAccountActionMock).toHaveBeenCalledTimes(1);
  });

  it("shows the success state with a login link after confirming", async () => {
    const user = userEvent.setup();
    await renderPage({ uid: UID, token: TOKEN });

    await user.click(screen.getByRole("button", { name: "Aktivera kontot" }));

    expect(
      await screen.findByRole("heading", { level: 1, name: "Kontot är aktiverat" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute("href", "/logga-in");
  });

  it("shows the returned error and keeps the confirm button on failure", async () => {
    confirmAccountActionMock.mockResolvedValueOnce({
      success: false,
      error: "Länken är ogiltig eller har gått ut. Registrera dig igen för att få en ny länk.",
    });
    const user = userEvent.setup();
    await renderPage({ uid: UID, token: TOKEN });

    await user.click(screen.getByRole("button", { name: "Aktivera kontot" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Länken är ogiltig eller har gått ut");
    expect(screen.getByRole("button", { name: "Aktivera kontot" })).toBeInTheDocument();
  });
});
