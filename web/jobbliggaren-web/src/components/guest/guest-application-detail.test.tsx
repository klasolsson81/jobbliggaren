import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestApplicationDetail } from "./guest-application-detail";
import { findGuestApplication } from "@/lib/guest/mock-data";

/**
 * #1516 — the detail modal's `Senast uppdaterad` renders the product's form.
 *
 * This site was never named in the issue, which counted two render sites where
 * there were four. It is a defect site all the same: measured rendered on
 * 2026-08-27 at `a49789c6`, `/gast/ansokningar/ga-2` showed
 * `SENAST UPPDATERAD · för 3 dagar sedan`.
 *
 * The application is fetched through `findGuestApplication` rather than built
 * by hand, so the test measures the row production actually renders
 * (AGENTS.md §5 `Tests:` — the premise must be one production produces).
 */
describe("GuestApplicationDetail — relative time (#1516)", () => {
  it("renders the catalogue's form for ga-2", () => {
    const application = findGuestApplication("ga-2");
    expect(application).not.toBeNull();

    render(<GuestApplicationDetail application={application!} />);

    expect(screen.getByText("3 dagar sedan")).toBeInTheDocument();
  });

  it("renders no `för …` form", () => {
    const application = findGuestApplication("ga-2");
    const { container } = render(
      <GuestApplicationDetail application={application!} />
    );

    expect(container.textContent).not.toMatch(/för \d/);
  });
});
