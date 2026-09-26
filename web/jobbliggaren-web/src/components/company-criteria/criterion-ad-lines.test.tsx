import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { CriterionAdLines } from "./criterion-ad-lines";

const ADS = { magnitude: 42, saturated: false, tooBroad: false, notMaterialised: false };
const MATCH_NOT_ASSESSED = { count: null, tooBroad: false, notMaterialised: false };

describe("CriterionAdLines — not assessed", () => {
  it("renders the nudge to match settings by default", () => {
    render(
      <CriterionAdLines
        criterionId="a"
        ads={ADS}
        matching={MATCH_NOT_ASSESSED}
        variant="standalone"
        adviceStatedByCaller={false}
        actionOfferedByCaller={false}
      />,
    );
    expect(screen.getByText(/Du har inte angett vilka yrken du söker inom/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Ställ in matchning" })).toBeInTheDocument();
  });

  it("renders no nudge with omitNotAssessedNudge, and keeps the ads line", () => {
    render(
      <CriterionAdLines
        criterionId="a"
        ads={ADS}
        matching={MATCH_NOT_ASSESSED}
        variant="standalone"
        adviceStatedByCaller={false}
        actionOfferedByCaller={false}
        omitNotAssessedNudge
      />,
    );
    expect(screen.getByRole("link", { name: "42 aktiva annonser" })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Ställ in matchning" })).toBeNull();
    expect(screen.queryByText(/vilka yrken du söker inom/)).toBeNull();
  });
});
