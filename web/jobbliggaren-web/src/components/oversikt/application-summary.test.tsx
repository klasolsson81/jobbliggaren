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
  it("visar alla sju poster även när bara en status har en grupp", () => {
    render(<ApplicationSummary pipeline={ok([group("Submitted", 2)])} />);

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
      />,
    );

    const terminal = screen.getByText("Avslut och vilande").closest("li");
    expect(terminal).not.toBeNull();
    expect(within(terminal as HTMLElement).getByText("7")).toBeInTheDocument();
    // Ett konto med enbart avslutade ansökningar får INTE läsa som tomt.
    expect(screen.getByText("7 ansökningar · 0 aktiva")).toBeInTheDocument();
  });

  it("visar tomt-läget med nästa steg när kontot saknar ansökningar", () => {
    render(<ApplicationSummary pipeline={ok([])} />);

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
    render(<ApplicationSummary pipeline={{ kind: "error" }} />);

    expect(
      screen.getByText(
        "Antalet ansökningar kunde inte hämtas. Använd Uppdatera för att försöka igen.",
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
      <ApplicationSummary pipeline={ok([group("Submitted", 2)])} />,
    );

    // Fem tomma aktiva steg + den terminala posten.
    expect(container.querySelectorAll('[data-empty="true"]')).toHaveLength(6);
  });
});
