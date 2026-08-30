import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { GuestDemoBanner } from "./guest-demo-banner";

describe("<GuestDemoBanner />", () => {
  it("renderar DEMO-etikett + civic-utility-text + skapa-konto-CTA", () => {
    render(<GuestDemoBanner />);
    expect(screen.getByText("DEMO")).toBeInTheDocument();
    expect(
      screen.getByText(/utforskar Jobbliggaren som gäst/i)
    ).toBeInTheDocument();
    const cta = screen.getByRole("link", { name: /skapa konto/i });
    expect(cta).toHaveAttribute("href", "/registrera");
  });

  it("har region-roll med svenskt aria-label så skärmläsare annonserar demoläget", () => {
    render(<GuestDemoBanner />);
    expect(screen.getByRole("region", { name: "Demoläge" })).toBeInTheDocument();
  });

  it("innehåller inget utropstecken eller emoji (civic-utility-disciplin)", () => {
    const { container } = render(<GuestDemoBanner />);
    const text = container.textContent ?? "";
    expect(text).not.toMatch(/!/);
    // No emoji range U+1F300–U+1FAFF + supplementary symbols
    expect(text).not.toMatch(
      /[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}]/u
    );
  });

  it("erbjuder en etiketterad väg ut ur demot till startsidan", () => {
    render(<GuestDemoBanner />);
    const toStart = screen.getByRole("link", { name: /till startsidan/i });
    expect(toStart).toHaveAttribute("href", "/");
  });

  // Ordningen är assertionen, inte en detalj: bandet har EN länkstil, så det som
  // skiljer utgången från primäråtgärden är att den står först. Listan pinnar
  // också antalet — en tredje länk i bandet faller här och ska granskas som en
  // egen fråga, inte glida in.
  it("radar utgången före skapa-konto så primäråtgärden står sist", () => {
    const { container } = render(<GuestDemoBanner />);
    const hrefs = Array.from(container.querySelectorAll("a")).map((a) =>
      a.getAttribute("href")
    );
    expect(hrefs).toEqual(["/", "/registrera"]);
  });
});
