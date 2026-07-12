import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { TemplateSchematic } from "./template-schematic";

/**
 * Ärlighetsgrindar för mallkortens mini-schematik (#820).
 *
 * Schematiken är det enda stället i produkten där vi RITAR en bild av vad PDF:en
 * kommer att bli. En vacker bild som lovar fel sak är värre än ingen bild alls —
 * därför pinnas de påståenden den får göra, inte bara att den renderar.
 */
describe("<TemplateSchematic /> — ärlighetskontrakt", () => {
  const NAMES = ["Klar", "Accentlinje", "MorkPanel"] as const;

  it.each(NAMES)(
    "%s renderar exakt en dekorativ svg (aria-hidden, ej i tillgänglighetsträdet)",
    (name) => {
      const { container } = render(<TemplateSchematic template={name} />);

      const svgs = container.querySelectorAll("svg");
      expect(svgs).toHaveLength(1);
      expect(svgs[0]).toHaveAttribute("aria-hidden", "true");
      expect(svgs[0]).toHaveClass("jp-schem");
    }
  );

  it.each(NAMES)(
    "%s ritar INGA bokstavsformer — bara abstrakta staplar (inget typsnittslöfte)",
    (name) => {
      const { container } = render(<TemplateSchematic template={name} />);

      // Modern och Klassisk löser båda ut till paketerade Lato idag, och TYPSNITT
      // saknar kontroll. Ett <text>-element skulle antyda ett typsnittsval.
      expect(container.querySelectorAll("text")).toHaveLength(0);
    }
  );

  it("Mörk panel ritar INGET foto — renderaren emitterar ingen fotoyta (PR-10, DPIA-grind)", () => {
    const { container } = render(<TemplateSchematic template="MorkPanel" />);

    // Den enklaste lögnen att råka berätta: varje mörk-panel-CV-mall i världen har
    // en avatarcirkel. Vår renderare ritar ingen. Alltså gör inte schematiken det.
    expect(container.querySelectorAll("circle")).toHaveLength(0);
    expect(container.querySelectorAll("image")).toHaveLength(0);
  });

  it("Mörk panels sidopanel ÄR accentfärgad och går kant till kant (Background(accent).ExtendVertical())", () => {
    const { container } = render(<TemplateSchematic template="MorkPanel" />);

    const panel = container.querySelector(".jp-schem__accent");
    expect(panel).not.toBeNull();
    // Kant till kant: från x=0, hela höjden. Namnet säger "mörk", renderingen säger
    // "accent" — schematiken följer renderingen, inte namnet.
    expect(panel).toHaveAttribute("x", "0");
    expect(panel).toHaveAttribute("y", "0");
    expect(panel).toHaveAttribute("height", "170");
  });

  it("Klar har accentfärgade understrykningar under rubrikerna; Accentlinje har det INTE", () => {
    const { container: klar } = render(<TemplateSchematic template="Klar" />);
    const { container: linje } = render(
      <TemplateSchematic template="Accentlinje" />
    );

    // Klar: LineHorizontal(0.75) i accent över hela spaltbredden (width=92, height=1).
    const klarUnderlines = [...klar.querySelectorAll(".jp-schem__accent")].filter(
      (r) => r.getAttribute("width") === "92" && r.getAttribute("height") === "1"
    );
    expect(klarUnderlines.length).toBeGreaterThan(0);

    // Accentlinje: streck FÖRE rubriken (3px brett), ingen understrykning.
    const linjeUnderlines = [
      ...linje.querySelectorAll(".jp-schem__accent"),
    ].filter(
      (r) => r.getAttribute("width") === "92" && r.getAttribute("height") === "1"
    );
    expect(linjeUnderlines).toHaveLength(0);

    const linjeBars = [...linje.querySelectorAll(".jp-schem__accent")].filter(
      (r) => r.getAttribute("width") === "3"
    );
    expect(linjeBars.length).toBeGreaterThan(0);
  });

  it("okänd mall gör INGET färgpåstående (noll accent-element) — fail-safe som AtsSafe => false", () => {
    const { container } = render(<TemplateSchematic template="FramtidaMall" />);

    // Vi vet inte hur en framtida BE-mall renderas → vi påstår ingenting om dess färg.
    expect(container.querySelectorAll(".jp-schem__accent")).toHaveLength(0);
    // Men den ritar fortfarande en neutral struktur (ingen tom ruta).
    expect(container.querySelectorAll("rect").length).toBeGreaterThan(3);
  });
});
