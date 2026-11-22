import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { z } from "zod";
import {
  DEFAULT_JUNIOR_JOBS_QUERY,
  isJuniorChoice,
  isJuniorJobAdId,
  isJuniorRuleId,
  JUNIOR_RULE_IDS,
  JUNIOR_TABS,
  juniorChoices,
  juniorJobsHref,
  parseJuniorJobsQuery,
  withPage,
  withTab,
} from "./junior-jobs";

const ROUTE = "/admin/juniorjobb";

describe("parseJuniorJobsQuery", () => {
  it("reads the bare route as the first page of Att avgöra", () => {
    expect(parseJuniorJobsQuery({})).toEqual(DEFAULT_JUNIOR_JOBS_QUERY);
    expect(DEFAULT_JUNIOR_JOBS_QUERY).toEqual({ tab: "unclear", page: 1 });
  });

  it.each([
    ["att-avgora", "unclear"],
    ["visas", "shown"],
    ["beslutade", "decided"],
  ] as const)("reads flik=%s as the %s tab", (slug, tab) => {
    expect(parseJuniorJobsQuery({ flik: slug }).tab).toBe(tab);
  });

  it.each(["Visas", "shown", "Unclear", "", "beslutade "])("reads an unknown tab %j as the default", (slug) => {
    expect(parseJuniorJobsQuery({ flik: slug }).tab).toBe("unclear");
  });

  it("reads the first of a repeated value", () => {
    expect(parseJuniorJobsQuery({ flik: ["visas", "beslutade"], sida: ["3", "4"] })).toEqual({ tab: "shown", page: 3 });
  });

  it.each([
    ["2", 2],
    ["1000", 1000],
    ["1001", 1],
    ["0", 1],
    ["-2", 1],
    ["2.5", 1],
    ["1e3", 1],
    ["abc", 1],
    ["99999", 1],
  ])("reads sida=%s as page %d, never a page the backend refuses", (raw, page) => {
    expect(parseJuniorJobsQuery({ sida: raw }).page).toBe(page);
  });
});

describe("juniorJobsHref", () => {
  it("leaves the defaults out, so the bare route is the first page of Att avgöra", () => {
    expect(juniorJobsHref(ROUTE, DEFAULT_JUNIOR_JOBS_QUERY)).toBe(ROUTE);
  });

  it("writes the tab in Swedish and the page, and reads back what it wrote", () => {
    for (const tab of JUNIOR_TABS) {
      const query = withPage(withTab(tab), 4);
      const href = juniorJobsHref(ROUTE, query);
      const params = Object.fromEntries(new URL(href, "https://example.test").searchParams);
      expect(parseJuniorJobsQuery(params)).toEqual(query);
    }
    expect(juniorJobsHref(ROUTE, { tab: "decided", page: 2 })).toBe(`${ROUTE}?flik=beslutade&sida=2`);
  });

  it("starts a tab from its first page", () => {
    expect(withTab("shown")).toEqual({ tab: "shown", page: 1 });
  });
});

describe("juniorChoices", () => {
  it("offers both decisions in Att avgöra", () => {
    expect(juniorChoices("unclear", null)).toEqual(["junior", "notJunior"]);
  });

  it("offers Inte junior in Visas, and the hand-back too once the row was decided", () => {
    expect(juniorChoices("shown", null)).toEqual(["notJunior"]);
    expect(juniorChoices("shown", "junior")).toEqual(["notJunior", null]);
  });

  it("offers the other decision first and the hand-back second in Beslutade", () => {
    expect(juniorChoices("decided", "junior")).toEqual(["notJunior", null]);
    expect(juniorChoices("decided", "notJunior")).toEqual(["junior", null]);
  });
});

describe("the action's input guards", () => {
  it("takes a decision or null, and nothing else", () => {
    expect([null, "junior", "notJunior"].every(isJuniorChoice)).toBe(true);
    expect(["Junior", "unclear", undefined, 1, ""].some(isJuniorChoice)).toBe(false);
  });

  it("takes an id of a GUID's shape only", () => {
    expect(isJuniorJobAdId("00000000-0000-4000-8000-000000000901")).toBe(true);
    expect(["../decision", "", "00000000-0000-4000-8000", 7].some(isJuniorJobAdId)).toBe(false);
  });
});

// ── The rule labels ──────────────────────────────────────────────────────────────────────────

const WEB_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");

function readJson(relative: string): unknown {
  return JSON.parse(readFileSync(path.resolve(WEB_ROOT, relative), "utf8"));
}

/** The rule file's ids, read leniently: everything else in it is the backend's. */
const ruleFileSchema = z.object({
  rules: z.array(z.object({ id: z.string() })),
  candidateFallbackRuleId: z.string(),
  noSignalRuleId: z.string(),
  timeoutRuleId: z.string(),
  reading: z.object({ ruleId: z.string() }),
});

/** Every id the backend can store as an assessment's rule: the file's rules, its fallbacks and the reading's. */
function ruleIds(): string[] {
  const file = ruleFileSchema.parse(readJson("../../src/Jobbliggaren.Infrastructure/JobAds/Junior/junior-rules.data-it.v1.json"));
  return [
    ...file.rules.map((rule) => rule.id),
    file.candidateFallbackRuleId,
    file.noSignalRuleId,
    file.timeoutRuleId,
    file.reading.ruleId,
  ];
}

const catalogueSchema = z.object({ juniorJobs: z.object({ rules: z.record(z.string(), z.string()) }) });

function ruleLabels(locale: "sv" | "en"): Readonly<Record<string, string>> {
  return catalogueSchema.parse(readJson(`messages/${locale}/admin.json`)).juniorJobs.rules;
}

describe("the junior rule labels (#2264)", () => {
  const ids = ruleIds();

  it("reads every rule id the rule file can store", () => {
    expect(ids.length).toBeGreaterThanOrEqual(16);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it.each(["sv", "en"] as const)("names every rule id in the %s label map", (locale) => {
    const labels = ruleLabels(locale);
    const missing = ids.filter((id) => (labels[id] ?? "").trim() === "");
    expect(missing).toEqual([]);
  });

  it("knows every rule id the rule file can store, so none reads as an unknown rule", () => {
    expect(ids.filter((id) => !isJuniorRuleId(id))).toEqual([]);
  });

  it("labels exactly the ids it knows, in both languages", () => {
    expect(Object.keys(ruleLabels("sv")).sort()).toEqual([...JUNIOR_RULE_IDS].sort());
    expect(Object.keys(ruleLabels("en")).sort()).toEqual([...JUNIOR_RULE_IDS].sort());
  });
});
