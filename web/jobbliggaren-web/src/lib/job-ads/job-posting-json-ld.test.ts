import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";
import { CODED_TAXONOMY_IDS } from "@/lib/i18n/coded-taxonomy";
import { parseAdDescription } from "@/components/job-ads/format-ad-description";
import {
  EMPLOYMENT_TYPE_BY_CONCEPT_ID,
  WORK_ABROAD_CONCEPT_ID,
  buildJobPosting,
  descriptionHtml,
  employmentTypesOf,
  escapeHtmlText,
} from "./job-posting-json-ld";

const HERE = dirname(fileURLToPath(import.meta.url));

/** The ten Klass 2 ids, read from the one source so this table cannot drift from it. */
const KLASS2 = JSON.parse(
  readFileSync(resolve(HERE, "../../../../../src/Jobbliggaren.Infrastructure/Taxonomy/klass2-taxonomy.json"), "utf8")
) as { employmentTypes: { conceptId: string; label: string }[]; worktimeExtents: { conceptId: string; label: string }[] };
const byLabel = (label: string): string =>
  [...KLASS2.employmentTypes, ...KLASS2.worktimeExtents].find((o) => o.label === label)?.conceptId ?? `missing:${label}`;

const AD: PublicJobAdDto = {
  id: "0b0b0b0b-1111-4222-8333-444444444444",
  title: "Sjuksköterska till vårdcentral",
  companyName: "Region Exempel",
  description: "Om tjänsten\n\nVi söker dig som vill arbeta med primärvård.\nDu trivs i team.\n\n- Legitimation\n- Körkort",
  url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
  publishedAt: "2026-10-01T08:00:00+00:00",
  expiresAt: "2026-11-01T08:00:00+00:00",
  municipalityName: "Stockholm",
  regionName: "Stockholms län",
  employmentTypeConceptId: byLabel("Vikariat"),
  worktimeExtentConceptId: byLabel("Heltid"),
};

/** The exact keys a posting may carry, nested, as the auditor's pin asks (M2). */
const ALLOWED_TOP_KEYS = [
  "@context",
  "@type",
  "title",
  "description",
  "datePosted",
  "validThrough",
  "hiringOrganization",
  "jobLocation",
  "employmentType",
];
const FORBIDDEN_KEYS = [
  "url",
  "identifier",
  "sameAs",
  "baseSalary",
  "directApply",
  "applicantLocationRequirements",
  "jobLocationType",
  "occupationalCategory",
  "industry",
  "taxID",
  "vatID",
  "leiCode",
  "logo",
  "streetAddress",
  "postalCode",
];

const allKeysOf = (value: unknown, acc: string[] = []): string[] => {
  if (Array.isArray(value)) value.forEach((v) => allKeysOf(v, acc));
  else if (value !== null && typeof value === "object") {
    for (const [k, v] of Object.entries(value)) {
      acc.push(k);
      allKeysOf(v, acc);
    }
  }
  return acc;
};

describe("buildJobPosting: the eligible ad", () => {
  const posting = buildJobPosting(AD);

  it("is a JobPosting with exactly the allowed keys, nested types included", () => {
    expect(posting).not.toBeNull();
    expect(Object.keys(posting!).sort()).toEqual([...ALLOWED_TOP_KEYS].sort());
    expect(posting!["@context"]).toBe("https://schema.org");
    expect(posting!["@type"]).toBe("JobPosting");
    expect(Object.keys(posting!.hiringOrganization).sort()).toEqual(["@type", "name"]);
    expect(posting!.hiringOrganization["@type"]).toBe("Organization");
    expect(Object.keys(posting!.jobLocation).sort()).toEqual(["@type", "address"]);
    expect(posting!.jobLocation["@type"]).toBe("Place");
    expect(Object.keys(posting!.jobLocation.address).sort()).toEqual(
      ["@type", "addressCountry", "addressLocality", "addressRegion"].sort()
    );
    expect(posting!.jobLocation.address["@type"]).toBe("PostalAddress");
  });

  it("carries none of the keys that would invent a fact or publish an id", () => {
    const keys = allKeysOf(posting);
    for (const forbidden of FORBIDDEN_KEYS) expect(keys).not.toContain(forbidden);
    expect(JSON.stringify(posting)).not.toContain(AD.url);
    expect(JSON.stringify(posting)).not.toContain(AD.id);
  });

  it("takes title, company and location from the DTO, with addressCountry SE and the län as region", () => {
    expect(posting!.title).toBe("Sjuksköterska till vårdcentral");
    expect(posting!.hiringOrganization.name).toBe("Region Exempel");
    expect(posting!.jobLocation.address).toEqual({
      "@type": "PostalAddress",
      addressLocality: "Stockholm",
      addressRegion: "Stockholms län",
      addressCountry: "SE",
    });
  });

  it("writes datePosted and validThrough as ISO 8601 from the DTO's instants", () => {
    expect(posting!.datePosted).toBe("2026-10-01T08:00:00.000Z");
    expect(posting!.validThrough).toBe("2026-11-01T08:00:00.000Z");
  });

  it("leaves validThrough out when the ad has no deadline (Google: do not include it)", () => {
    const open = buildJobPosting({ ...AD, expiresAt: null });
    expect(open).not.toBeNull();
    expect("validThrough" in open!).toBe(false);
  });

  it("leaves addressRegion out when the län is not resolved", () => {
    const noRegion = buildJobPosting({ ...AD, regionName: null });
    expect(noRegion!.jobLocation.address).toEqual({
      "@type": "PostalAddress",
      addressLocality: "Stockholm",
      addressCountry: "SE",
    });
  });

  it("maps Vikariat + Heltid to FULL_TIME then TEMPORARY, extent first", () => {
    expect(posting!.employmentType).toEqual(["FULL_TIME", "TEMPORARY"]);
  });

  it("leaves employmentType out when nothing maps", () => {
    const permanent = buildJobPosting({
      ...AD,
      employmentTypeConceptId: byLabel("Tillsvidareanställning (inkl. eventuell provanställning)"),
      worktimeExtentConceptId: null,
    });
    expect(permanent).not.toBeNull();
    expect("employmentType" in permanent!).toBe(false);
  });
});

describe("buildJobPosting: eligibility, one branch each", () => {
  it.each([
    ["a blank title", { title: "   " }],
    ["a blank company name", { companyName: "   " }],
    ["no resolved kommun (a län alone does not carry addressCountry)", { municipalityName: null }],
    ["a kommun name that is only whitespace", { municipalityName: " " }],
    ["a description that is the title again (case-insensitive)", { description: " SJUKSKÖTERSKA TILL VÅRDCENTRAL " }],
    ["a description with no block", { description: "\n\n  \n" }],
    ["Arbete utomlands as the employment type", { employmentTypeConceptId: WORK_ABROAD_CONCEPT_ID }],
  ] as const)("returns null for %s", (_label, patch) => {
    expect(buildJobPosting({ ...AD, ...patch })).toBeNull();
  });

  it("does not need a region, an employment type or an extent", () => {
    expect(
      buildJobPosting({ ...AD, regionName: null, employmentTypeConceptId: null, worktimeExtentConceptId: null })
    ).not.toBeNull();
  });
});

describe("the employment-type table", () => {
  it("covers every Klass 2 id in klass2-taxonomy.json and nothing else", () => {
    const source = [...KLASS2.employmentTypes, ...KLASS2.worktimeExtents].map((o) => o.conceptId).sort();
    expect(Object.keys(EMPLOYMENT_TYPE_BY_CONCEPT_ID).sort()).toEqual(source);
    expect([...CODED_TAXONOMY_IDS].sort()).toEqual(source);
  });

  it.each([
    ["Heltid", ["FULL_TIME"]],
    ["Deltid", ["PART_TIME"]],
    ["Tidsbegränsad anställning", ["TEMPORARY"]],
    ["Vikariat", ["TEMPORARY"]],
    ["Säsongsanställning", ["TEMPORARY"]],
    ["Sommarjobb / feriejobb", ["TEMPORARY"]],
    ["Behovsanställning", ["OTHER"]],
    ["Vanlig anställning", []],
    ["Tillsvidareanställning (inkl. eventuell provanställning)", []],
    ["Arbete utomlands", []],
  ])("maps %s to %j (senior-cto-advisor F3, 2026-10-17)", (label, expected) => {
    expect(EMPLOYMENT_TYPE_BY_CONCEPT_ID[byLabel(label) as keyof typeof EMPLOYMENT_TYPE_BY_CONCEPT_ID]).toEqual(expected);
  });

  it("never emits PER_DIEM (paid by the day is not what an on-call job says)", () => {
    expect(Object.values(EMPLOYMENT_TYPE_BY_CONCEPT_ID).flat()).not.toContain("PER_DIEM");
  });

  it("ignores an id the catalogue does not know, deduplicates, and keeps the extent first", () => {
    expect(employmentTypesOf({ employmentTypeConceptId: "drifted_id", worktimeExtentConceptId: byLabel("Deltid") })).toEqual(["PART_TIME"]);
    expect(employmentTypesOf({ employmentTypeConceptId: byLabel("Vikariat"), worktimeExtentConceptId: byLabel("Heltid") })).toEqual([
      "FULL_TIME",
      "TEMPORARY",
    ]);
    expect(employmentTypesOf({ employmentTypeConceptId: null, worktimeExtentConceptId: null })).toEqual([]);
  });
});

describe("the description HTML", () => {
  it("is the page's own blocks in Google's tag set: a heading as <p>, a list as <ul><li>, a soft break as <br>", () => {
    expect(descriptionHtml(AD.description)).toBe(
      "<p>Om tjänsten</p>" +
        "<p>Vi söker dig som vill arbeta med primärvård.<br>Du trivs i team.</p>" +
        "<ul><li>Legitimation</li><li>Körkort</li></ul>"
    );
  });

  it("agrees with parseAdDescription block for block: the text sequence is the same", () => {
    const html = descriptionHtml(AD.description)!;
    const texts = html
      .replace(/<br>/g, "\n")
      .split(/<\/?(?:p|ul|li)>/)
      .map((s) => s.trim())
      .filter((s) => s !== "");
    const blocks = parseAdDescription(AD.description).flatMap((b) =>
      b.kind === "list" ? b.items : [b.text]
    );
    expect(texts).toEqual(blocks);
  });

  it("escapes every text run before wrapping it, so ad text can never be live markup (security-auditor M1)", () => {
    const hostile =
      'Se <script>alert(1)</script> här\n\n- <a href="javascript:x">klicka</a>\n- <img src=x onerror=y>\n\nTom & Jerry\'s "citat"';
    const html = descriptionHtml(hostile)!;
    expect(html).not.toMatch(/<(script|a|img)\b/i);
    expect(html).toContain("&lt;script&gt;alert(1)&lt;/script&gt;");
    expect(html).toContain("&lt;a href=&quot;javascript:x&quot;&gt;");
    expect(html).toContain("Tom &amp; Jerry&#39;s &quot;citat&quot;");
    expect(html.match(/<\/?[a-z]+>?/g)?.every((tag) => /^<\/?(p|ul|li|br)>$/.test(tag))).toBe(true);
  });

  it("is null for a description with no block", () => {
    expect(descriptionHtml("")).toBeNull();
    expect(descriptionHtml(" \n\n ")).toBeNull();
  });

  it("escapes the five characters and nothing else", () => {
    expect(escapeHtmlText("a & b < c > d \" e ' f åäö")).toBe("a &amp; b &lt; c &gt; d &quot; e &#39; f åäö");
  });
});
