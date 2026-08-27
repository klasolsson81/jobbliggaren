import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestAnsokningarPage } from "./guest-ansokningar-page";

/**
 * #1516 — gäst-pipelinens tidskolumn renderar produktens form.
 *
 * Detta är den yta defekten mättes på: sju rader i EN kolumn, där tre bar
 * `idag`/`igår` och fyra bar `för N dagar sedan`. Assertionen nedan är
 * defekten uttryckt som ett test — alla tre formerna i samma vy, och ingen
 * `för`-prefixad. En form-assertion per sträng hade inte fångat den, för varje
 * enskild sträng var välformad svenska; det var kombinationen som var fel.
 */
describe("GuestAnsokningarPage — relativ tid (#1516)", () => {
  it("renderar katalogens tre former i samma vy", () => {
    render(<GuestAnsokningarPage />);

    // ga-3 (0 dagar), ga-1 (1 dag), ga-2 (3 dagar) mot mockens frusna referens.
    expect(screen.getByText("idag")).toBeInTheDocument();
    expect(screen.getByText("igår")).toBeInTheDocument();
    expect(screen.getByText("3 dagar sedan")).toBeInTheDocument();
  });

  it("renderar ingen `för …`-form någonstans på sidan", () => {
    const { container } = render(<GuestAnsokningarPage />);

    // Bred med flit: `för` följt av en siffra fångar både "för 3 dagar sedan"
    // och "för 1 vecka sedan", och skulle fånga en ny variant ingen räknat upp.
    expect(container.textContent).not.toMatch(/\bför \d/);
  });

  it("uttrycker sjudagarsintervallet i dagar, inte i veckor", () => {
    // ga-6 bar "för 1 vecka sedan", som katalogens plural inte kan uttrycka.
    render(<GuestAnsokningarPage />);

    expect(screen.getByText("7 dagar sedan")).toBeInTheDocument();
    expect(screen.queryByText(/vecka/)).not.toBeInTheDocument();
  });
});
