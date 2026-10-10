import { describe, it, expect, vi } from "vitest";
import { render } from "@testing-library/react";
import { CvReviewPanel } from "./cv-review-panel";
import type { CvReviewDto } from "@/lib/dto/parsed-resume";

/**
 * The PII boundary of the review ledger (#2083, senior-cto-advisor condition 6): the client island
 * receives the rows as server-rendered nodes, and no prop it reads as data carries CV text. The
 * nodes themselves are excluded from the check — they are the rendered output, which reaches the
 * browser either way.
 */

const ledgerProps = vi.fn();
vi.mock("@/components/resumes/cv-review-ledger", () => ({
  CvReviewLedger: (props: Record<string, unknown>) => {
    ledgerProps(props);
    return null;
  },
}));
vi.mock("@/components/resumes/cv-review-profile-toggle", () => ({
  CvReviewProfileToggle: () => null,
}));

const QUOTE = "Minskade svarstiden med 40 procent";
const NOTE = "Kvantifierade uppgifter i 3 av 3 roller.";
const OBSERVATION = "Senaste rollen står först.";

const REVIEW: CvReviewDto = {
  rubricVersion: "2.3.0",
  profile: "Ats",
  categories: [
    { category: "Content", band: "Competitive", passCount: 1, warnCount: 0, failCount: 0, notAssessedCount: 0 },
    { category: "Structure", band: "TopTier", passCount: 1, warnCount: 0, failCount: 0, notAssessedCount: 0 },
  ],
  verdicts: [
    {
      criterionId: "A1", name: "Mätbara resultat", category: "Content", verdict: "Pass",
      evidence: [{ kind: "TextSpan", start: 0, length: QUOTE.length, quote: QUOTE, note: NOTE, observation: null, isExcerpt: false }],
      notAssessedReason: null, notAssessedKind: null, passCriterion: null,
      userStatus: null, userStatusStaleAt: null, isIgnorable: false,
    },
    {
      criterionId: "B7", name: "Kronologi tydlig", category: "Structure", verdict: "Pass",
      evidence: [{ kind: "Structural", start: null, length: null, quote: null, note: null, observation: OBSERVATION, isExcerpt: false }],
      notAssessedReason: null, notAssessedKind: null, passCriterion: null,
      userStatus: null, userStatusStaleAt: null, isIgnorable: false,
    },
  ],
  criticalFails: [],
  assessedCount: 2,
  totalCount: 2,
};

/** Every prop value the island reads as data: everything but the rendered nodes. */
function dataOf(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(dataOf);
  if (value !== null && typeof value === "object" && !("$$typeof" in value)) {
    return Object.fromEntries(
      Object.entries(value)
        .filter(([key]) => key !== "node" && key !== "notice")
        .map(([key, inner]) => [key, dataOf(inner)]),
    );
  }
  return value;
}

describe("CvReviewPanel — what the ledger island receives", () => {
  it("rows as rendered nodes beside three closed-vocabulary fields, and no CV text as data", () => {
    render(
      <CvReviewPanel review={REVIEW} target={{ kind: "canonical", resumeId: "r-1" }} profile="Ats" />,
    );

    const props = ledgerProps.mock.calls[0]![0] as { groups: Array<{ rows: Array<Record<string, unknown>> }> };
    expect(Object.keys(props.groups[0]!.rows[0]!).sort()).toEqual(["category", "criterionId", "node", "verdict"]);

    const data = JSON.stringify(dataOf(props));
    for (const text of [QUOTE, NOTE, OBSERVATION]) {
      expect(data).not.toContain(text);
    }
  });
});
