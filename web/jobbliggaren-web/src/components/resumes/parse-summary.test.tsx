import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { ParseSummary } from "./parse-summary";
import type {
  AutoPromoteBlockReason,
  OverallConfidenceLevel,
  ParseConfidenceDto,
} from "@/lib/dto/parsed-resume";

function confidence(overall: OverallConfidenceLevel): ParseConfidenceDto {
  return {
    overall,
    requiresManualReview: overall !== "Confident",
    fallback: "None",
    sections: [],
  };
}

function lede(
  overall: OverallConfidenceLevel,
  blockReason: AutoPromoteBlockReason | null,
): Element | null {
  const { container } = render(
    <ParseSummary confidence={confidence(overall)} blockReason={blockReason} />,
  );
  return container.querySelector(".jp-parse-summary__lede");
}

describe("ParseSummary", () => {
  it("renders no lede for a Confident parse", () => {
    expect(lede("Confident", null)).toBeNull();
  });

  it("renders the Degraded lede", () => {
    expect(lede("Degraded", "IncompleteContent")).toHaveTextContent(
      "Rätta filen och ladda upp den på nytt.",
    );
  });

  it("renders no lede for a Failed parse when the block reason is ParseNotConfident", () => {
    expect(lede("Failed", "ParseNotConfident")).toBeNull();
  });

  it("renders the Failed lede when the block reason is PersonnummerPresent", () => {
    expect(lede("Failed", "PersonnummerPresent")).toHaveTextContent(
      "Spara om filen som PDF eller Word och ladda upp den på nytt.",
    );
  });
});
