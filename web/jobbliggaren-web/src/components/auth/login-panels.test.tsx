import { describe, expect, it } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { DeadCodePanel } from "./dead-code-panel";
import { LoginFlowNotice } from "./login-flow-notice";
import { LoginOutcomePanel } from "./login-outcome-panel";
import { ProviderButtons } from "./provider-buttons";

describe("LoginOutcomePanel", () => {
  it("states a pending deletion with the date in Swedish form and a way to reach us", async () => {
    render(
      <LoginOutcomePanel
        result={{ outcome: "pendingDeletion", permanentDeletionDate: "2026-10-19" }}
      />
    );

    const panel = screen.getByRole("status");
    // The panel's first sentence IS its heading; nothing restates it.
    expect(
      screen.getByRole("heading", { level: 2, name: "Ditt konto raderas permanent 19 okt. 2026." })
    ).toBeInTheDocument();
    expect(panel).toHaveTextContent("Fram till dess kan du få det återställt genom att mejla");
    expect(screen.getByRole("link", { name: "kontakt@jobbliggaren.se" })).toHaveAttribute(
      "href",
      "mailto:kontakt@jobbliggaren.se"
    );
    // No "Ångra" that does not exist: restoring goes through support.
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    await waitFor(() => expect(panel).toHaveFocus());
  });

  it("states closed registration without an error channel, with the way home OUTSIDE the live region", () => {
    render(<LoginOutcomePanel result={{ outcome: "registrationClosed" }} />);

    const panel = screen.getByRole("status");
    expect(
      screen.getByRole("heading", { level: 2, name: "Registreringen är inte öppen ännu." })
    ).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    const home = screen.getByRole("link", { name: "Till startsidan" });
    expect(home).toHaveAttribute("href", "/");
    expect(panel).not.toContainElement(home);
  });

  it("states an unavailable account with a coarse horizon and the contact address", () => {
    render(<LoginOutcomePanel result={{ outcome: "accountUnavailable" }} />);

    expect(
      screen.getByRole("heading", {
        level: 2,
        name: "Vi kan inte logga in på den här adressen just nu.",
      })
    ).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent(
      "Försök igen senare, eller kontakta oss på kontakt@jobbliggaren.se."
    );
  });
});

describe("DeadCodePanel", () => {
  it("names no cause for an expired code, because the page cannot know one", async () => {
    render(<DeadCodePanel reason="expired" />);

    const panel = screen.getByRole("status");
    expect(panel).toHaveTextContent(
      "Koden går inte att använda längre. Skicka en ny kod och försök igen."
    );
    expect(panel).not.toHaveTextContent(/gått ut|15 minuter/);
    // It replaces the field, not the form: the step's h1 still stands, so no heading here.
    expect(screen.queryByRole("heading")).not.toBeInTheDocument();
    await waitFor(() => expect(panel).toHaveFocus());
  });

  it("names the link for a burned code: the burn is the code's alone", () => {
    render(<DeadCodePanel reason="burned" />);

    expect(screen.getByRole("status")).toHaveTextContent(
      "Innehåller mejlet en inloggningslänk kan du använda den i stället"
    );
  });
});

describe("LoginFlowNotice", () => {
  it.each([
    ["grantUnusable", "Registreringen slutfördes inte", "Skriv in din e-postadress nedan och börja om."],
    ["codeExpired", "Inloggningen gick ut", "En inloggning gäller i 15 minuter."],
  ] as const)("%s names the remedy that is on THIS page and takes focus", async (notice, title, body) => {
    render(<LoginFlowNotice notice={notice} />);

    const panel = screen.getByRole("status");
    expect(screen.getByRole("heading", { level: 2, name: title })).toBeInTheDocument();
    expect(panel).toHaveTextContent(body);
    expect(panel).toHaveTextContent("Skriv in din e-postadress nedan och börja om.");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    await waitFor(() => expect(panel).toHaveFocus());
  });

  it.each([
    ["externalUnverified", "Google kan inte intyga din e-postadress", "Logga in med en kod i stället: skriv in din e-postadress nedan."],
    ["externalNotCompleted", "Inloggningen med Google slutfördes inte", "Försök igen med Google, eller skriv in din e-postadress nedan."],
  ] as const)("%s names its provider and the remedy on THIS page, and takes focus", async (notice, title, body) => {
    render(<LoginFlowNotice notice={notice} provider="google" />);

    const panel = screen.getByRole("status");
    expect(screen.getByRole("heading", { level: 2, name: title })).toBeInTheDocument();
    expect(panel).toHaveTextContent(body);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    await waitFor(() => expect(panel).toHaveFocus());
  });

  it("says the account is deleted, with the restore route as a mail link, and takes focus", async () => {
    render(<LoginFlowNotice notice="accountDeleted" />);

    const panel = screen.getByRole("status");
    expect(screen.getByRole("heading", { level: 2, name: "Ditt konto är raderat" })).toBeInTheDocument();
    expect(panel).toHaveTextContent("I 30 dagar kan du få kontot återställt");
    expect(within(panel).getByRole("link", { name: "kontakt@jobbliggaren.se" })).toHaveAttribute(
      "href",
      "mailto:kontakt@jobbliggaren.se"
    );
    await waitFor(() => expect(panel).toHaveFocus());
  });
});

describe("ProviderButtons", () => {
  it("renders the three providers in reach order, each saying it is not there yet", () => {
    render(<ProviderButtons />);

    expect(screen.getAllByRole("button").map((button) => button.textContent)).toEqual([
      "Fortsätt med GoogleKommer snart",
      "Fortsätt med LinkedInKommer snart",
      "Fortsätt med GitHubKommer snart",
    ]);
  });

  it("keeps each row in the tab order and in the accessibility tree: aria-disabled, never disabled", () => {
    render(<ProviderButtons />);

    for (const button of screen.getAllByRole("button")) {
      expect(button).toHaveAttribute("aria-disabled", "true");
      expect(button).not.toBeDisabled();
      expect(button).toHaveAttribute("type", "button");
      expect(button).toHaveAttribute("data-variant", "outline");
    }
  });

  it("draws no provider mark while the rows are inactive", () => {
    const { container } = render(<ProviderButtons />);

    expect(container.querySelector("svg, img")).toBeNull();
  });
});
