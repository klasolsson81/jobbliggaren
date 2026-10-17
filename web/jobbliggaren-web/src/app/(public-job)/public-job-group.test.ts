import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import enPublicJob from "../../../messages/en/publicjob.json";
import svPublicJob from "../../../messages/sv/publicjob.json";

/**
 * Fitness functions for the public job page's route group (ADR 0165), derived from the filesystem.
 *
 * What these protect is what a Server Component page cannot recover once it has started to stream: a
 * `loading.tsx` or a Suspense boundary above a `notFound()` or a thrown error commits a 200 first, so the
 * 404 and the 5xx become a 200 with a noindex tag, and an outage reads to a crawler as a page that is
 * simply there (Next streaming guide, "Status codes"). And what keeps the page per-request: ISR or a
 * static render would hand out a copy of an ad after its erasure (ADR 0160 D6, D9.4).
 */
const GROUP = resolve(dirname(fileURLToPath(import.meta.url)));
const SRC = resolve(GROUP, "..", "..");
const toPosix = (p: string) => p.split(sep).join("/");
const isTestFile = (name: string) => /\.(test|spec)\.(ts|tsx)$/.test(name);

function files(dir: string, acc: string[] = []): string[] {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) files(full, acc);
    else acc.push(full);
  }
  return acc;
}

const groupFiles = files(GROUP).filter((f) => !isTestFile(f));
const groupSources = groupFiles.filter((f) => /\.(ts|tsx)$/.test(f));
const read = (file: string) => readFileSync(file, "utf8");

/** Everything that decides what the public page shows or reads, beyond the group itself. */
const PUBLIC_SURFACE = [
  ...groupSources,
  resolve(SRC, "components/job-ads/public-job-ad.tsx"),
  resolve(SRC, "components/job-ads/strip-bounce-marker.tsx"),
  resolve(SRC, "lib/api/public-job-ad.ts"),
  resolve(SRC, "lib/api/public-job-ad-probe.ts"),
  resolve(SRC, "lib/auth/public-job-ad-route.ts"),
  resolve(SRC, "lib/job-ads/job-posting-json-ld.ts"),
];

describe("(public-job) route group", () => {
  it("finds the group's pages (the derivation is not vacuous)", () => {
    const pages = groupFiles.map((f) => toPosix(relative(GROUP, f))).filter((f) => f.endsWith("/page.tsx"));
    expect(pages.sort()).toEqual(["publik/borttagen/page.tsx", "publik/jobb/[id]/page.tsx", "publik/overbelastad/page.tsx"]);
  });

  it("has no loading.tsx anywhere: a Suspense fallback commits 200 before the status can be settled", () => {
    expect(groupFiles.filter((f) => /(^|[\\/])loading\.(tsx|ts|jsx|js)$/.test(f))).toEqual([]);
  });

  it("uses no Suspense boundary of its own above the ad", () => {
    expect(PUBLIC_SURFACE.filter((f) => /<Suspense\b|from "react".*\bSuspense\b/.test(read(f)))).toEqual([]);
  });

  it("never renders ad text as HTML; the JobPosting data block goes through JsonLdScript alone (ADR 0171)", () => {
    expect(PUBLIC_SURFACE.filter((f) => read(f).includes("dangerouslySetInnerHTML"))).toEqual([]);
    const withJsonLd = PUBLIC_SURFACE.filter((f) => /ld\+json|JsonLdScript/.test(read(f))).map((f) => toPosix(relative(SRC, f)));
    expect(withJsonLd).toEqual(["components/job-ads/public-job-ad.tsx"]);
    expect(read(resolve(SRC, "components/job-ads/public-job-ad.tsx"))).toMatch(/import \{ JsonLdScript \} from "@\/components\/seo\/json-ld-script"/);
  });

  it("builds the JobPosting from the DTO alone: no clock, no status, no fetch, no request state (ADR 0171)", () => {
    const mapper = read(resolve(SRC, "lib/job-ads/job-posting-json-ld.ts"))
      .replace(/\/\*[\s\S]*?\*\//g, "")
      .replace(/^\s*\/\/.*$/gm, "");
    expect(mapper).not.toMatch(/Date\.now\(|new Date\(\)|fetch\(|headers\(|cookies\(|\bstatus\b|expiresAt\s*[<>]/);
  });

  it("is rendered per request: no ISR, no static params, no force-static, no fetch revalidation", () => {
    const offenders = PUBLIC_SURFACE.filter((f) =>
      /export const (revalidate|dynamic)\b|generateStaticParams|revalidate\s*:|next:\s*\{|force-cache|export const dynamicParams/.test(
        read(f).replace(/\/\*[\s\S]*?\*\//g, "").replace(/^\s*\/\/.*$/gm, "")
      )
    );
    expect(offenders.map((f) => toPosix(relative(SRC, f)))).toEqual([]);
  });

  it("reads the ad through the one cached reader and nowhere else", () => {
    const readers = PUBLIC_SURFACE.filter((f) => /\/api\/v1\/public\/job-ads/.test(read(f))).map((f) => toPosix(relative(SRC, f)));
    expect(readers.sort()).toEqual(["lib/api/public-job-ad-probe.ts", "lib/api/public-job-ad.ts"]);
  });

  it("logs nothing on the page's own path: no console call carries an ad's title, company or description", () => {
    const withConsole = PUBLIC_SURFACE.filter((f) => /console\.(log|info|warn|error|debug)/.test(read(f)));
    expect(withConsole.map((f) => toPosix(relative(SRC, f)))).toEqual([]);
  });

  it("gives the 410 page no way to know which ad it answers for", () => {
    const gone = read(resolve(GROUP, "publik/borttagen/page.tsx"));
    expect(gone).not.toMatch(/params|getPublicJobAd|searchParams|headers\(|cookies\(/);
  });
});

describe("publicjob messages", () => {
  const leaves = (node: unknown, prefix = ""): string[] =>
    node !== null && typeof node === "object"
      ? Object.entries(node).flatMap(([k, v]) => leaves(v, prefix ? `${prefix}.${k}` : k))
      : [prefix];

  it("have the same keys in Swedish and English and no empty value", () => {
    expect(leaves(enPublicJob).sort()).toEqual(leaves(svPublicJob).sort());
    for (const catalogue of [svPublicJob, enPublicJob]) {
      expect(JSON.stringify(catalogue)).not.toMatch(/:\s*""/);
    }
  });
});
