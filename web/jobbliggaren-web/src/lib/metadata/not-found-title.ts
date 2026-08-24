import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

/**
 * The document title every 404 surface carries, from one source (CTO bind
 * 2026-08-24, follow-up debt 2 of #1495; design-reviewer Blocker 1, WCAG 2.4.2).
 *
 * Three kinds of file call this: the root `not-found.tsx` (every unmatched URL),
 * `(guest)/gast/not-found.tsx`, and the six retired CV routes whose whole body is
 * a session gate plus `notFound()`. They are one document semantically — same
 * `fallback.notFound.*` copy, same purpose: the address does not exist — so they
 * share one title by design, not by omission.
 *
 * `absolute` rather than a plain string, and that is load-bearing in BOTH
 * directions. Measured in a production build on Next 16.3.0, 2026-08-24: the root
 * `not-found.tsx` DOES receive the root layout's `title.template` (a plain
 * `"Sidan finns inte"` renders `Sidan finns inte | Jobbliggaren`) while
 * `(guest)/gast/not-found.tsx` does NOT (the same plain string renders bare). One
 * form cannot be correct for both — `absolute` opts out of the template, so the
 * composed string below is what ships from either, and a future Next version that
 * starts applying the template to a not-found boundary cannot turn it into
 * `Sidan finns inte | Jobbliggaren | Jobbliggaren`.
 *
 * The brand and the separator are NOT duplicated here: the string is composed by
 * running `metadata.titleTemplate` — the very value the root layout hands Next —
 * over the 404 heading. A replacer function, not a string, is the second argument
 * so a `$` in the heading can never be read as a capture reference.
 *
 * `(app)/not-found.tsx` deliberately does NOT call this: its own metadata is
 * measurably inert, because it is reached by a `notFound()` thrown mid-stream
 * (the response is 200, not 404) after the head has already flushed.
 */
export async function notFoundMetadata(): Promise<Metadata> {
  const tFallback = await getTranslations("fallback");
  const tMetadata = await getTranslations("metadata");
  const heading = tFallback("notFound.title");

  return {
    title: {
      absolute: tMetadata("titleTemplate").replace("%s", () => heading),
    },
  };
}
