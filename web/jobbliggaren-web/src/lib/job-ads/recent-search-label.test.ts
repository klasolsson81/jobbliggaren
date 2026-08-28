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
  return {
    all: c.all,
    remoteLeading: c.remoteLeading,
    remoteInline: c.remoteInline,
    or: c.or,
    separator: c.separator,
    more: (count) => c.more.replace("{count}", String(count)),
  };
}

const sv = copyFrom(svJobads);
const en = copyFrom(enJobads);

const named = (text: string, moreCount = 0): RecentSearchLabelPart => ({
  kind: "Named",
  text,
  moreCount,
});
const remote: RecentSearchLabelPart = { kind: "Remote", text: null, moreCount: 0 };

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
        "Tillsvidare +1 till, Heltid",
        label("Dimensions", "Conjunction", named("Tillsvidare", 1), named("Heltid")),
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
      [
        "Tillsvidare +1 more, Heltid",
        label("Dimensions", "Conjunction", named("Tillsvidare", 1), named("Heltid")),
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

  // Positionen, inte en flagga på wire:n, avgör skiftläget. Samma del, två platser.
  it("versaliserar distans-delen bara där den leder, och bara i sv", () => {
    const leading = label("Dimensions", "Disjunction", remote, named("Göteborg"));
    const trailing = label("Dimensions", "Disjunction", named("Göteborg"), remote);

    expect(buildRecentSearchLabel(leading, sv)).toBe("Distans eller Göteborg");
    expect(buildRecentSearchLabel(trailing, sv)).toBe("Göteborg eller distans");

    // Engelskan gemenar aldrig — båda formerna är "remote" utom där meningen börjar.
    expect(buildRecentSearchLabel(leading, en)).toBe("Remote or Göteborg");
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

  // Katalogerna måste bära exakt samma nyckeluppsättning — en nyckel som bara finns i sv
  // ger en engelsk användare en tom sträng mitt i labeln, tyst.
  it("har identiska label-nycklar i båda katalogerna", () => {
    expect(Object.keys(enJobads.recent.label).sort()).toEqual(
      Object.keys(svJobads.recent.label).sort(),
    );
  });
});
