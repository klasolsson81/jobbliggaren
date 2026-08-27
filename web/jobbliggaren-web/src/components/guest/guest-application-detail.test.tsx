import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestApplicationDetail } from "./guest-application-detail";
import { findGuestApplication } from "@/lib/guest/mock-data";

/**
 * #1516 — detaljmodalens `Senast uppdaterad` renderar produktens form.
 *
 * Denna site namngavs aldrig i issuen, som räknade två renderingssiter där det
 * fanns fyra. Den är ändå en defekt site: mätt renderat 2026-08-27 vid
 * `a49789c6` visade `/gast/ansokningar/ga-2` texten `SENAST UPPDATERAD ·
 * för 3 dagar sedan`.
 *
 * Ansökan hämtas genom `findGuestApplication` i stället för att byggas för
 * hand, så testet mäter den rad som faktiskt renderas i produktionen
 * (AGENTS.md §5 `Tests:` — premissen ska vara en produktionen producerar).
 */
describe("GuestApplicationDetail — relativ tid (#1516)", () => {
  it("renderar katalogens form för ga-2", () => {
    const application = findGuestApplication("ga-2");
    expect(application).not.toBeNull();

    render(<GuestApplicationDetail application={application!} />);

    expect(screen.getByText("3 dagar sedan")).toBeInTheDocument();
  });

  it("renderar ingen `för …`-form", () => {
    const application = findGuestApplication("ga-2");
    const { container } = render(
      <GuestApplicationDetail application={application!} />
    );

    expect(container.textContent).not.toMatch(/för \d/);
  });
});
