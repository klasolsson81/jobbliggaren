import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestOversiktPage } from "./guest-oversikt-page";

/**
 * #1516 — översiktens `Senast uppdaterat CV` renderar produktens form.
 *
 * Fjärde renderingssiten, och den andra issuen inte räknade. Den är särskild
 * på ett sätt: raden står som granne till `Demo aktiv sedan`, som hämtar sitt
 * `idag` ur `guest.oversikt.timeToday` i stället för ur data. Båda renderar
 * `idag` i dag, och det är ett sammanfall — CTO-bindet 2026-08-27 lät den
 * andra stå kvar eftersom den bär ett valt ord, inte en härledd fras.
 *
 * Därför assertar detta test på raden, inte på förekomsten av ordet `idag`:
 * en assertion på ordet ensamt hade passerat även om denna site slutat renderas.
 */
describe("GuestOversiktPage — relativ tid (#1516)", () => {
  it("Senast uppdaterat CV renderar den härledda formen", () => {
    render(<GuestOversiktPage />);

    // gr-1 ligger på referensdatumet -> `idag`, härlett via formatDaysAgo.
    const row = screen.getByText("Senast uppdaterat CV").closest("*");
    expect(row).not.toBeNull();
    expect(row!.parentElement?.textContent).toContain("idag");
  });

  it("renderar ingen `för …`-form någonstans på sidan", () => {
    const { container } = render(<GuestOversiktPage />);

    expect(container.textContent).not.toMatch(/för \d/);
  });
});
