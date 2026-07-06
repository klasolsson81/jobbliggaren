import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import type { ActionResult } from "@/lib/actions/_action-result";
import svPages from "../../../../messages/sv/pages.json";
import BekraftaEpostPage from "./page";

// #679 — the PUBLIC /bekrafta-epost landing + its confirm island. The page is an
// async Server Component using getTranslations; mock it to a real Swedish translator
// so page copy matches the island copy (the island uses useTranslations via the
// render-intl provider). The public confirm action is mocked so no fetch runs, and so
// the no-auto-POST invariant can be asserted (the action must fire only on click).

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace: string) =>
    createTranslator({
      locale: "sv",
      messages: { [namespace]: svPages },
      namespace,
    }),
}));

const confirmEmailChangeActionMock =
  vi.fn<(uid: string, email: string, token: string) => Promise<ActionResult>>();

vi.mock("@/lib/actions/confirm-email-change", () => ({
  confirmEmailChangeAction: (uid: string, email: string, token: string) =>
    confirmEmailChangeActionMock(uid, email, token),
}));

const UID = "0af1b2c3d4e5460788990011aabbccdd";
const EMAIL = "ny.adress@exempel.se";
const TOKEN = "Q2ZERjQ4-token-base64url";

type SearchParams = Record<string, string | string[] | undefined>;

async function renderPage(searchParams: SearchParams) {
  const ui = await BekraftaEpostPage({
    searchParams: Promise.resolve(searchParams),
  });
  return render(ui);
}

describe("BekraftaEpostPage (#679 public confirm landing)", () => {
  beforeEach(() => {
    confirmEmailChangeActionMock.mockReset();
    confirmEmailChangeActionMock.mockResolvedValue({ success: true });
  });

  it("shows an invalid-link state and does NOT POST when params are missing", async () => {
    await renderPage({});

    expect(
      screen.getByRole("heading", { level: 1, name: "Länken går inte att använda" }),
    ).toBeInTheDocument();
    // No confirm affordance and no POST for a garbled/absent link.
    expect(
      screen.queryByRole("button", { name: "Bekräfta ny e-postadress" }),
    ).not.toBeInTheDocument();
    expect(confirmEmailChangeActionMock).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in",
    );
  });

  it("treats a partial link (uid only) as invalid", async () => {
    await renderPage({ uid: UID });

    expect(
      screen.getByRole("heading", { level: 1, name: "Länken går inte att använda" }),
    ).toBeInTheDocument();
    expect(confirmEmailChangeActionMock).not.toHaveBeenCalled();
  });

  it("renders the confirm button but does NOT auto-POST on load when params are present", async () => {
    await renderPage({ uid: UID, email: EMAIL, token: TOKEN });

    expect(
      screen.getByRole("button", { name: "Bekräfta ny e-postadress" }),
    ).toBeInTheDocument();
    // CRITICAL: the confirm POST must never fire on page load (mail scanners /
    // prefetchers GET the link and would auto-consume the single-use token).
    expect(confirmEmailChangeActionMock).not.toHaveBeenCalled();
  });

  it("fires the confirm action with the exact triple only on button click", async () => {
    const user = userEvent.setup();
    await renderPage({ uid: UID, email: EMAIL, token: TOKEN });

    await user.click(screen.getByRole("button", { name: "Bekräfta ny e-postadress" }));

    await waitFor(() =>
      expect(confirmEmailChangeActionMock).toHaveBeenCalledWith(UID, EMAIL, TOKEN),
    );
    expect(confirmEmailChangeActionMock).toHaveBeenCalledTimes(1);
  });

  it("shows the success state with a login link after confirming", async () => {
    const user = userEvent.setup();
    await renderPage({ uid: UID, email: EMAIL, token: TOKEN });

    await user.click(screen.getByRole("button", { name: "Bekräfta ny e-postadress" }));

    expect(
      await screen.findByRole("heading", { level: 1, name: "E-postadressen är bytt" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Logga in" })).toHaveAttribute(
      "href",
      "/logga-in",
    );
  });

  it("shows the returned error and keeps the confirm button on failure", async () => {
    confirmEmailChangeActionMock.mockResolvedValueOnce({
      success: false,
      error: "Länken är ogiltig eller har gått ut. Logga in och begär ett nytt adressbyte under Inställningar.",
    });
    const user = userEvent.setup();
    await renderPage({ uid: UID, email: EMAIL, token: TOKEN });

    await user.click(screen.getByRole("button", { name: "Bekräfta ny e-postadress" }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Länken är ogiltig eller har gått ut");
    // The button stays so a transient failure can be retried.
    expect(
      screen.getByRole("button", { name: "Bekräfta ny e-postadress" }),
    ).toBeInTheDocument();
  });
});
