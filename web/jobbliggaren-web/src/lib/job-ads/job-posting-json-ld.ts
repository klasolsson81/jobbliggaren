import { parseAdDescription } from "@/components/job-ads/format-ad-description";
import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";
import { type CodedTaxonomyId, isCodedTaxonomyId } from "@/lib/i18n/coded-taxonomy";

/**
 * The `JobPosting` structured data of one listed ad on the public job page (#2090 S3, ADR 0171).
 *
 * A pure mapping over the public DTO: no clock, no status, no fetch. Whether the ad is PUBLIC is
 * the backend's one predicate (ADR 0160 D2), and a DTO only exists for a Listed ad; what this module
 * decides is whether that DTO is COMPLETE enough for Google's schema. When it is not, the answer is
 * `null`, the page carries no script and stays indexable.
 *
 * Every value is one the page shows (Google: "all information in the markup must be visible on the
 * job page"), and nothing is invented: no salary, no `directApply`, no `identifier`, no
 * `jobLocationType`, no `url`. The description is the page's own blocks (`parseAdDescription`) as
 * the HTML subset Google reads, with every text run escaped BEFORE a tag is put around it, so ad text
 * can never become live markup on a consumer that renders the string (security-auditor M1).
 *
 * Google's requirements: https://developers.google.com/search/docs/appearance/structured-data/job-posting
 * (page dated 2026-09-08, read 2026-10-17).
 */

/** Google's closed, case-sensitive vocabulary for `employmentType`. One or more values are allowed. */
export type GoogleEmploymentType =
  | "FULL_TIME"
  | "PART_TIME"
  | "CONTRACTOR"
  | "TEMPORARY"
  | "INTERN"
  | "VOLUNTEER"
  | "PER_DIEM"
  | "OTHER";

/**
 * Arbete utomlands. An ad with this employment type is ineligible: `jobLocation.addressCountry` would
 * have to say `SE`, and for work abroad that is an invented fact (senior-cto-advisor F3/F6).
 */
export const WORK_ABROAD_CONCEPT_ID = "9Wuo_2Yb_36E" satisfies CodedTaxonomyId;

/**
 * Klass 2 → Google, keyed on the frozen concept id (never on a label: the Swedish label is the
 * backend's, the English one the catalogue's, and neither is a key). Exhaustive over
 * `CodedTaxonomyId`, so a new id is a compile error here. The extent values say how much; the
 * contract values say for how long. Permanence is not a Google axis, so a permanent form maps to
 * nothing and the extent alone carries what is true. Behovsanställning is `OTHER`: Google defines
 * `PER_DIEM` as paid by the day, which an on-call job does not say.
 */
export const EMPLOYMENT_TYPE_BY_CONCEPT_ID: Readonly<Record<CodedTaxonomyId, readonly GoogleEmploymentType[]>> = {
  "6YE1_gAC_R2G": ["FULL_TIME"], // Heltid
  "947z_JGS_Uk2": ["PART_TIME"], // Deltid
  "sTu5_NBQ_udq": ["TEMPORARY"], // Tidsbegränsad anställning
  "gro4_cWF_6D7": ["TEMPORARY"], // Vikariat
  "EBhX_Qm2_8eX": ["TEMPORARY"], // Säsongsanställning
  "Jh8f_q9J_pbJ": ["TEMPORARY"], // Sommarjobb / feriejobb
  "1paU_aCR_nGn": ["OTHER"], // Behovsanställning
  "PFZr_Syz_cUq": [], // Vanlig anställning
  "kpPX_CNN_gDU": [], // Tillsvidareanställning (inkl. eventuell provanställning)
  [WORK_ABROAD_CONCEPT_ID]: [], // Arbete utomlands: the whole ad is ineligible
};

/** The exact shape emitted. Optional members are absent, never `undefined`, so a key pin can be exact. */
export type JobPostingJsonLd = {
  readonly "@context": "https://schema.org";
  readonly "@type": "JobPosting";
  readonly title: string;
  readonly description: string;
  readonly datePosted: string;
  readonly validThrough?: string;
  readonly hiringOrganization: { readonly "@type": "Organization"; readonly name: string };
  readonly jobLocation: {
    readonly "@type": "Place";
    readonly address: {
      readonly "@type": "PostalAddress";
      readonly addressLocality: string;
      readonly addressRegion?: string;
      readonly addressCountry: "SE";
    };
  };
  readonly employmentType?: GoogleEmploymentType[];
};

/** `& < > " '` as entities. Applied to every text run before any tag is wrapped around it. */
export function escapeHtmlText(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

/**
 * The description as the HTML subset Google reads (`p`, `ul`, `li`), built from the same blocks the
 * page renders, so markup and page cannot drift. A heading block is a `<p>` (Google ignores heading
 * tags, and the text is part of the ad); a soft line break inside a paragraph is a `<br>`, which is
 * what the page's `pre-line` shows. `null` when the text has no block at all.
 */
export function descriptionHtml(description: string): string | null {
  const blocks = parseAdDescription(description);
  if (blocks.length === 0) return null;
  return blocks
    .map((block) => {
      switch (block.kind) {
        case "heading":
          return `<p>${escapeHtmlText(block.text)}</p>`;
        case "list":
          return `<ul>${block.items.map((item) => `<li>${escapeHtmlText(item)}</li>`).join("")}</ul>`;
        case "paragraph":
          return `<p>${block.text.split("\n").map(escapeHtmlText).join("<br>")}</p>`;
      }
    })
    .join("");
}

/**
 * The `employmentType` values for an ad: the extent first, then the contract form, each only when
 * the id is a known Klass 2 id, deduplicated. An id the catalogue does not know (taxonomy drift)
 * contributes nothing and never fails the page.
 */
export function employmentTypesOf(jobAd: Pick<PublicJobAdDto, "employmentTypeConceptId" | "worktimeExtentConceptId">): GoogleEmploymentType[] {
  const values: GoogleEmploymentType[] = [];
  for (const id of [jobAd.worktimeExtentConceptId, jobAd.employmentTypeConceptId]) {
    if (id === null || !isCodedTaxonomyId(id)) continue;
    for (const value of EMPLOYMENT_TYPE_BY_CONCEPT_ID[id]) {
      if (!values.includes(value)) values.push(value);
    }
  }
  return values;
}

const normalised = (text: string): string => text.trim().toLowerCase();

/**
 * The `JobPosting` for a listed ad, or `null` when the DTO is not complete enough for Google's
 * required properties: a non-blank title, a non-blank company name, a description with at least one
 * block that is not just the title again, a resolved kommun (the one `addressCountry: SE` rests on;
 * a län alone does not qualify, senior-cto-advisor F4), and an employment type that is not work
 * abroad. `validThrough` is present only when the ad has a deadline (a listed ad's deadline is never
 * in the past, ADR 0160 D2).
 */
export function buildJobPosting(jobAd: PublicJobAdDto): JobPostingJsonLd | null {
  const title = jobAd.title.trim();
  const companyName = jobAd.companyName.trim();
  const municipality = jobAd.municipalityName?.trim() ?? "";
  if (title === "" || companyName === "" || municipality === "") return null;
  if (jobAd.employmentTypeConceptId === WORK_ABROAD_CONCEPT_ID) return null;
  if (normalised(jobAd.description) === normalised(jobAd.title)) return null;

  const description = descriptionHtml(jobAd.description);
  if (description === null) return null;

  const region = jobAd.regionName?.trim() ?? "";
  const address: JobPostingJsonLd["jobLocation"]["address"] = {
    "@type": "PostalAddress",
    addressLocality: municipality,
    ...(region === "" ? {} : { addressRegion: region }),
    addressCountry: "SE",
  };

  const employmentType = employmentTypesOf(jobAd);

  return {
    "@context": "https://schema.org",
    "@type": "JobPosting",
    title,
    description,
    datePosted: new Date(jobAd.publishedAt).toISOString(),
    ...(jobAd.expiresAt === null ? {} : { validThrough: new Date(jobAd.expiresAt).toISOString() }),
    hiringOrganization: { "@type": "Organization", name: companyName },
    jobLocation: { "@type": "Place", address },
    ...(employmentType.length === 0 ? {} : { employmentType }),
  };
}
