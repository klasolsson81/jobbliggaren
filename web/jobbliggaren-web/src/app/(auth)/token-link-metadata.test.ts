import { describe, it, expect, vi } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import type { Metadata } from "next";

// #706 / PR #1313 (security-auditor Major, 2026-08-11) — every (auth) page whose URL
// carries an emailed token must pin BOTH metadata legs: robots noindex AND
// referrer "no-referrer". The Referer leg exists because
// Referrer-Policy: strict-origin-when-cross-origin strips path+query only cross-origin,
// and the edge persists unredacted Referer on 5xx (Caddy http.log.error) even with no
// `log` directive. Discovery is SHAPE-based and reaches exactly: pages at ANY depth
// under (auth)/ whose source declares a `token?:` searchParam. A token route added in
// another route group, or reading a token in another shape, is OUTSIDE this reach and
// must extend the glob/shape when added — the limit is recorded on #706.

vi.mock("next-intl/server", () => ({
  getTranslations: async () => () => "",
}));

const pageImporters = import.meta.glob("./**/page.tsx");

const authDir = path.dirname(fileURLToPath(import.meta.url));

const tokenPages = Object.entries(pageImporters)
  .filter(([key]) => /\btoken\?\s*:/.test(readFileSync(path.join(authDir, key), "utf8")))
  .sort(([a], [b]) => a.localeCompare(b));

describe("token-carrying (auth) pages — metadata invariants (#706)", () => {
  it("discovery reaches at least the three known token routes (no vacuous pass)", () => {
    expect(tokenPages.map(([key]) => key)).toEqual(
      expect.arrayContaining([
        "./aterstall-losenord/page.tsx",
        "./bekrafta-epost/page.tsx",
        "./bekrafta-konto/page.tsx",
      ]),
    );
  });

  it.each(tokenPages)(
    "%s sets robots noindex AND referrer no-referrer",
    async (_key, importer) => {
      // These pages moved from `export const metadata` to `generateMetadata` when they
      // gained a translated document title (WCAG 2.4.2): a title read from the message
      // catalogue is async, and Next allows only one of the two exports per file. The
      // invariant this test guards is unchanged; only where it is read from moved.
      const mod = (await importer()) as {
        generateMetadata?: () => Promise<Metadata>;
      };
      expect(typeof mod.generateMetadata).toBe("function");

      const metadata = await mod.generateMetadata!();
      expect(metadata.robots).toEqual({ index: false, follow: false });
      expect(metadata.referrer).toBe("no-referrer");
    },
  );
});
