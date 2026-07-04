/**
 * Shared return contract for form/mutation server actions (#612). The identical
 * `export type ActionResult` was previously re-declared in
 * `applications`/`me`/`match-preferences`/`resumes`; it now lives here and those
 * modules re-export it for their existing consumers.
 *
 * Payload-carrying and domain-named variants (e.g. `CreateApplicationFromJobAdResult`,
 * `SaveJobAdResult`, `CvSuggestResult`) stay local to their action file — they are
 * distinct contracts, not this same knowledge piece.
 */
export type ActionResult =
  | { success: true }
  | { success: false; error: string };
