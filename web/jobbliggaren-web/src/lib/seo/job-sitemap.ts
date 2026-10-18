import { absoluteUrl } from "@/lib/site-url";

/**
 * Where the sitemap's files live (ADR 0174). Every file sits at the site root: unless it is submitted through Search
 * Console, a sitemap covers only what lies under its own directory (Google, "Build and submit a sitemap"), and the
 * job pages are `/jobb/<uuid>`. Never under `/jobb`, which is a protected prefix.
 */
export const SITEMAP_INDEX_PATH = "/sitemap.xml";
export const MARKETING_SITEMAP_PATH = "/sitemap-sidor.xml";

/**
 * How many parts the job URLs are split into. The API's `ListedJobAdSnapshotLimits.PartCount`; the architecture test
 * `ListedJobAdSnapshotBoundaryTests` holds the two equal, and one route folder exists per part.
 */
export const JOB_SITEMAP_PART_COUNT = 2;

export function jobSitemapPath(part: number): `/${string}` {
  return `/sitemap-jobb-${part}.xml`;
}

/** The parts, numbered from 1. */
export function jobSitemapParts(): number[] {
  return Array.from({ length: JOB_SITEMAP_PART_COUNT }, (_, index) => index + 1);
}

/** What the index lists: the marketing part, then every job part, as absolute URLs. */
export function sitemapIndexLocs(): string[] {
  return [absoluteUrl(MARKETING_SITEMAP_PATH), ...jobSitemapParts().map((part) => absoluteUrl(jobSitemapPath(part)))];
}
