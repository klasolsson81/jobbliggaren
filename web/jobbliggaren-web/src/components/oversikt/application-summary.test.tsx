import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { ApplicationSummary } from "./application-summary";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

// Grupper byggs som backend producerar dem: GroupBy(Status) utelämnar tomma
// statusar helt. Se pipeline-counts.test.ts för varför det är premissen.
function group(status: ApplicationStatus, count: number): PipelineGroupDto {
  return { status, count, applications: [] };
}

function ok(groups: PipelineGroupDto[]): ApiResult<PipelineGroupDto[]> {
  return { kind: "ok", data: groups };
}

const STEPS = [
  "Utkast",
  "Skickad",
  "Bekräftad",
  "Intervju bokad",
  "Pågående intervju",
  "Erbjudande",
];

describe("ApplicationSummary", () => {
  it("linkHref={null} renderar ingen ankarlänk", () => {
    // Grenen finns för ytor där etiketten inte har någon sann destination (#1572).
    render(
      <ApplicationSummary pipeline={ok([group("Submitted", 2)])} linkHref={null} />,
    );

    expect(
      screen.queryByRole("link", { name: "Visa alla ansökningar" }),
    ).toBeNull();
    // Sammanfattningen i övrigt oförändrad — annars mäter testet att inget renderades.
    expect(
      screen.getByRole("list", { name: "Ansökningar per steg" }),
    ).toBeInTheDocument();
  });

  it("visar alla sju poster även när bara en status har en grupp", () => {
    render(<ApplicationSummary pipeline={ok([group("Submitted", 2)])} linkHref="/ansokningar" />);

    const list = screen.getByRole("list", { name: "Ansökningar per steg" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(7);
    for (const name of STEPS) {
      expect(within(list).getByText(name)).toBeInTheDocument();
    }
    expect(within(list).getByText("Avslut och vilande")).toBeInTheDocument();
  });

  it("ankarraden räknar totalt över alla tio och aktiva över de sex", () => {
    render(
      <ApplicationSummary
        pipeline={ok([
          group("Submitted", 2),
          group("Acknowledged", 1),
          group("Rejected", 1),
        ])}
        linkHref="/ansokningar"
      />,
    );

    expect(screen.getByText("4 ansökningar · 3 aktiva")).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Visa alla ansökningar" }),
    ).toHaveAttribute("href", "/ansokningar");
  });

  it("rullar ihop de fyra terminala statusarna till en post", () => {
    render(
      <ApplicationSummary
        pipeline={ok([
          group("Rejected", 3),
          group("Accepted", 1),
          group("Withdrawn", 1),
          group("Ghosted", 2),
        ])}
        linkHref="/ansokningar"
      />,
    );

    const terminal = screen.getByText("Avslut och vilande").closest("li");
    expect(terminal).not.toBeNull();
    expect(within(terminal as HTMLElement).getByText("7")).toBeInTheDocument();
    // Ett konto med enbart avslutade ansökningar får INTE läsa som tomt.
    expect(screen.getByText("7 ansökningar · 0 aktiva")).toBeInTheDocument();
  });

  it("visar tomt-läget med nästa steg när kontot saknar ansökningar", () => {
    render(<ApplicationSummary pipeline={ok([])} linkHref="/ansokningar" />);

    expect(
      screen.getByText("Du har inga ansökningar än"),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ny ansökan" })).toHaveAttribute(
      "href",
      "/ny-ansokan",
    );
    expect(screen.queryByRole("list")).toBeNull();
  });

  it("säger att antalet inte kunde hämtas i stället för att påstå noll", () => {
    render(<ApplicationSummary pipeline={{ kind: "error" }} linkHref="/ansokningar" />);

    expect(
      screen.getByText(
        "Antalet ansökningar kunde inte hämtas. Uppdatera sidan.",
      ),
    ).toBeInTheDocument();
    // Fabrikation: en degraderad hämtning får aldrig rendera en siffra.
    expect(screen.queryByText(/ansökningar ·/)).toBeNull();
    expect(screen.queryByRole("list")).toBeNull();
  });

  it("markerar nollsteg med data-empty", () => {
    // Kontrastgarantin bor i CSS-regeln och verifieras renderat, inte här:
    // vitest laddar aldrig globals.css. Detta test mäter bara markeringen.
    const { container } = render(
      <ApplicationSummary pipeline={ok([group("Submitted", 2)])} linkHref="/ansokningar" />,
    );

    // Fem tomma aktiva steg + den terminala posten.
    expect(container.querySelectorAll('[data-empty="true"]')).toHaveLength(6);
  });

  // ── #1717: det här blocket bär INGEN rubrik, och det är strukturellt ────────────────────────
  // Komponenten har ingen rubrikprop över huvud taget. Regeln `design-reviewer` band är
  // rubrik-per-innehållstyp: sektionens h2 "Mina ansökningar" står ensam över en enda
  // innehållstyp och namnger redan blocket, så en h3 vore en tautologi (A1). En prop vars enda
  // producerbara värde var `null` hade dessutom lämnat en gren produktionen aldrig når och
  // §5 `Tests:` förbjuder en fixtur att framkalla — den är borttagen i stället för pinnad.

  it("renderar ingen h3 — sektionens h2 namnger redan blocket", () => {
    render(
      <ApplicationSummary
        pipeline={ok([group("Submitted", 2)])}
        linkHref="/ansokningar"
      />,
    );

    expect(document.querySelector(".jp-appsummary__heading")).toBeNull();
    expect(screen.queryByRole("heading", { level: 3 })).not.toBeInTheDocument();
    // Kontroll: blocket renderade faktiskt, annars mäter frånvaron ovan ingenting.
    expect(document.querySelector(".jp-appsummary__totals")).not.toBeNull();
  });

  it("ohämtbar-läget är ORÖRT av #1717 — fortfarande en ensam <p>", () => {
    // Pinnen på att blocket renderar byte-identiskt efter #1717. Komponentens egen diff mot basen
    // är tom, så det här pinnar formen snarare än att upptäcka den — men faller den, har någon
    // gett blocket en rubrikgren den inte ska ha.
    render(
      <ApplicationSummary pipeline={{ kind: "error" }} linkHref="/ansokningar" />,
    );

    const root = document.querySelector(".jp-appsummary");
    expect(root?.tagName).toBe("P");
    expect(root).toHaveClass("jp-appsummary--unavailable");
    expect(document.querySelector(".jp-appsummary__heading")).toBeNull();
  });
});
