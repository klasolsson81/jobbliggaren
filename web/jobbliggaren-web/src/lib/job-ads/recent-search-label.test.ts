import { describe, it, expect } from "vitest";
import svJobads from "../../../messages/sv/jobads.json";
import enJobads from "../../../messages/en/jobads.json";
import type { RecentSearchLabel, RecentSearchLabelPart } from "@/lib/dto/recent-searches";
import {
  buildRecentSearchLabel,
  type RecentSearchLabelCopy,
} from "./recent-search-label";

/**
 * Copy read from the REAL catalogues, not a hand-written stand-in. That is deliberate: the
 * separator and the disjunction joiner carry their own spacing (" eller " / ", "), and a
 * stripped space is invisible in a JSON diff. Reading the shipped values means losing one
 * fails here rather than on the page.
 */
function copyFrom(catalogue: typeof svJobads): RecentSearchLabelCopy {
  const c = catalogue.recent.label;
  const coded: Record<string, string> = catalogue.enums.codedTaxonomy;
  return {
    all: c.all,
    remoteLeading: c.remoteLeading,
    remoteInline: c.remoteInline,
    or: c.or,
    separator: c.separator,
    more: (count) => c.more.replace("{count}", String(count)),
    // Same doctrine as the words above: the shipped values, not a stand-in. A missing key
    // renders a visible sentinel so the assertion names it instead of printing `undefined`.
    coded: (conceptId) => coded[conceptId] ?? `?${conceptId}`,
  };
}

const sv = copyFrom(svJobads);
const en = copyFrom(enJobads);

const named = (text: string, moreCount = 0): RecentSearchLabelPart => ({
  kind: "Named",
  text,
  conceptId: null,
  moreCount,
});
const remote: RecentSearchLabelPart = {
  kind: "Remote",
  text: null,
  conceptId: null,
  moreCount: 0,
};
// The refinement axes ship a code, not a name (#1537). Real ids from
// `klass2-taxonomy.json`, so these rows pin what production actually emits.
const PERMANENT = "kpPX_CNN_gDU";
const FULL_TIME = "6YE1_gAC_R2G";
const coded = (conceptId: string, moreCount = 0): RecentSearchLabelPart => ({
  kind: "Coded",
  text: null,
  conceptId,
  moreCount,
});

const label = (
  kind: RecentSearchLabel["kind"],
  join: RecentSearchLabel["join"],
  ...parts: RecentSearchLabelPart[]
): RecentSearchLabel => ({ kind, join, parts });

describe("buildRecentSearchLabel", () => {
  // Regressionspinnen: varje förväntan här är ORDAGRANT vad C#-sidans DeriveLabel
  // producerade före #1430. Svenskan får inte ha rört sig en byte — bara engelskan är ny.
  describe("sv — oförändrad mot den strängen backend byggde före #1430", () => {
    it.each([
      ["Alla annonser", label("All", "None")],
      ["backend", label("Query", "None", named("backend"))],
      ["Data/IT", label("OccupationField", "None", named("Data/IT"))],
      ["Göteborg", label("Dimensions", "None", named("Göteborg"))],
      ["Distans", label("Dimensions", "None", remote)],
      ["Göteborg +1 till", label("Dimensions", "None", named("Göteborg", 1))],
      [
        "Göteborg eller Västra Götaland",
        label("Dimensions", "Disjunction", named("Göteborg"), named("Västra Götaland")),
      ],
      [
        "Göteborg eller distans",
        label("Dimensions", "Disjunction", named("Göteborg"), remote),
      ],
      [
        "Göteborg, Malmö eller distans",
        label("Dimensions", "Disjunction", named("Göteborg"), named("Malmö"), remote),
      ],
      [
        "Göteborg +1 till eller distans",
        label("Dimensions", "Disjunction", named("Göteborg", 1), remote),
      ],
      [
        "Tillsvidareanställning (inkl. eventuell provanställning) +1 till, Heltid",
        label("Dimensions", "Conjunction", coded(PERMANENT, 1), coded(FULL_TIME)),
      ],
    ])("renderar %j", (expected, input) => {
      expect(buildRecentSearchLabel(input, sv)).toBe(expected);
    });
  });

  describe("en — Klas-satt copy 2026-08-28", () => {
    it.each([
      ["All job ads", label("All", "None")],
      ["Remote", label("Dimensions", "None", remote)],
      ["Göteborg or remote", label("Dimensions", "Disjunction", named("Göteborg"), remote)],
      [
        "Göteborg, Malmö or remote",
        label("Dimensions", "Disjunction", named("Göteborg"), named("Malmö"), remote),
      ],
      [
        "Göteborg +1 more or remote",
        label("Dimensions", "Disjunction", named("Göteborg", 1), remote),
      ],
      // #1537 — the row this issue was filed for. Before the coded part it rendered
      // "Tillsvidareanställning (inkl. eventuell provanställning) +1 more, Heltid".
      [
        "Permanent employment (including any trial employment) +1 more, Full time",
        label("Dimensions", "Conjunction", coded(PERMANENT, 1), coded(FULL_TIME)),
      ],
    ])("renderar %j", (expected, input) => {
      expect(buildRecentSearchLabel(input, en)).toBe(expected);
    });
  });

  // Egennamnsdata ur taxonomin — ortnamn, länsnamn, yrkesgruppsnamn — är INTE copy och
  // översätts aldrig. Det är hela avgränsningen i #1430: orden runt namnen rör sig, namnen
  // står stilla.
  it("låter taxonomi-namn stå oöversatta i båda locales", () => {
    const input = label("Dimensions", "Disjunction", named("Västra Götaland"), remote);

    expect(buildRecentSearchLabel(input, sv)).toContain("Västra Götaland");
    expect(buildRecentSearchLabel(input, en)).toContain("Västra Götaland");
  });

  // Positionen, inte en flagga på wire:n, avgör vilken form distans-delen får. Den
  // efterställda är den enda FLERDELS-formen produktionen kan emittera: DeriveOrtLabel lägger
  // delarna kommun → län → distans, så en Remote-del står först bara när den är ensam — och
  // den formen är redan pinnad av it.each-raderna "Distans" och "Remote" ovan.
  it("ger distans-delen sin efterställda form i båda locales", () => {
    const trailing = label("Dimensions", "Disjunction", named("Göteborg"), remote);

    expect(buildRecentSearchLabel(trailing, sv)).toBe("Göteborg eller distans");
    expect(buildRecentSearchLabel(trailing, en)).toBe("Göteborg or remote");
  });

  // Fogningen är SEMANTIK, inte ett tecken: ort unioneras (alternativ), förfiningsaxlarna
  // AND:as (håller samtidigt). Samma delar, två sanningar, två renderingar.
  it("renderar samma delar olika beroende på fogningens innebörd", () => {
    const parts = [named("A"), named("B")] as const;

    expect(buildRecentSearchLabel(label("Dimensions", "Disjunction", ...parts), sv)).toBe(
      "A eller B",
    );
    expect(buildRecentSearchLabel(label("Dimensions", "Conjunction", ...parts), sv)).toBe(
      "A, B",
    );
  });
});
