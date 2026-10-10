import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { CvCriterionRow } from "./cv-criterion-row";
import type { CvCriterionVerdictDto } from "@/lib/dto/parsed-resume";

function verdict(overrides: Partial<CvCriterionVerdictDto> = {}): CvCriterionVerdictDto {
  return {
    criterionId: "A1",
    name: "Mätbara resultat",
    category: "Content",
    verdict: "Fail",
    evidence: [
      {
        kind: "TextSpan",
        start: 0,
        length: 10,
        quote: "Ansvarade för försäljning",
        note: "Ingen roll har ett mätbart resultat.",
        observation: null,
        isExcerpt: false,
      },
    ],
    notAssessedReason: null,
    userStatus: null,
    userStatusStaleAt: null,
    isIgnorable: false,
    ...overrides,
  };
}

function renderRow(props: Partial<Parameters<typeof CvCriterionRow>[0]> = {}) {
  return render(
    <table>
      <tbody>
        <CvCriterionRow verdict={verdict()} hasActionColumn={false} {...props} />
      </tbody>
    </table>,
  );
}

describe("CvCriterionRow", () => {
  it("leads with the readable name; the id stays a demoted reference", () => {
    renderRow();
    const header = screen.getByRole("rowheader");
    expect(within(header).getByText("Mätbara resultat")).toHaveAttribute("id", "cvledger-A1-name");
    expect(within(header).getByText("A1")).toHaveClass("jp-cvledger__id");
  });

  it("carries the outcome in words, not colour alone", () => {
    renderRow();
    expect(screen.getAllByRole("cell")[0]).toHaveTextContent("Underkänt");
  });

  it("puts the diagnosis before the quote it rests on", () => {
    renderRow();
    const item = document.querySelector(".jp-cvledger__evidence-item");
    expect(Array.from(item?.children ?? []).map((el) => el.tagName)).toEqual(["P", "BLOCKQUOTE"]);
  });

  it("shows a structural observation as the diagnosis", () => {
    renderRow({
      verdict: verdict({
        verdict: "Pass",
        evidence: [
          { kind: "Structural", start: null, length: null, quote: null, note: null, observation: "Kontaktuppgifterna står överst.", isExcerpt: false },
        ],
      }),
    });
    expect(screen.getByText("Kontaktuppgifterna står överst.")).toBeInTheDocument();
    expect(document.querySelector("blockquote")).toBeNull();
  });

  it("Ej bedömt shows its reason and no evidence list", () => {
    renderRow({
      verdict: verdict({ verdict: "NotAssessed", evidence: [], notAssessedReason: "Bedöms mot en jobbannons." }),
    });
    expect(screen.getAllByRole("cell")[0]).toHaveTextContent("Ej bedömt");
    expect(screen.getByText("Bedöms mot en jobbannons.")).toHaveClass("jp-cvledger__reason");
    expect(document.querySelector(".jp-cvledger__evidence-list")).toBeNull();
  });

  it("renders the action cell only where the table has the column", () => {
    const { unmount } = renderRow({ hasActionColumn: false });
    expect(screen.getAllByRole("cell")).toHaveLength(2);
    unmount();

    renderRow({ hasActionColumn: true, action: <button type="button">Gör något</button> });
    expect(screen.getAllByRole("cell")).toHaveLength(3);
    expect(screen.getByRole("button", { name: "Gör något" })).toBeInTheDocument();
  });
});
