import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@/lib/actions/me", () => ({ deleteAccountAction: vi.fn() }));
vi.mock("@/lib/auth/reauth-actions", () => ({ requestReauthCode: vi.fn() }));

import { PrivacyCard } from "./privacy-card";

// ADR 0144 row 19, as security-auditor signed it (#1891 form round).
const CONTACT_ROUTE = "Vill du ha en kopia av dina data kan du mejla kontakt@jobbliggaren.se.";

describe("PrivacyCard", () => {
  it("is one card, Sekretess och data, with the export and the deletion as its groups", () => {
    render(<PrivacyCard userEmail="anna@example.se" />);
    expect(screen.getByRole("heading", { level: 2, name: "Sekretess och data" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 3, name: "Radera ditt konto" })).toBeInTheDocument();
  });

  it("marks the export as not built yet, in words and without disabling it", () => {
    render(<PrivacyCard userEmail="anna@example.se" />);
    const exportButton = screen.getByRole("button", { name: /Exportera mina data/ });
    expect(exportButton).toHaveAccessibleName("Exportera mina data Kommer snart");
    expect(exportButton).toHaveAttribute("aria-disabled", "true");
    expect(exportButton).not.toHaveAttribute("title");
  });

  // Row 19: until the export exists, the route that works today is visible at it, without a click.
  it("names kontakt@ as the route to a copy, visible at the export and as its description", () => {
    render(<PrivacyCard userEmail="anna@example.se" />);
    const exportButton = screen.getByRole("button", { name: /Exportera mina data/ });
    expect(exportButton).toHaveAccessibleDescription(CONTACT_ROUTE);
    expect(screen.getByText(/Vill du ha en kopia av dina data/)).toBeVisible();
    expect(screen.getByRole("link", { name: "kontakt@jobbliggaren.se" })).toHaveAttribute(
      "href",
      "mailto:kontakt@jobbliggaren.se",
    );
  });
});
