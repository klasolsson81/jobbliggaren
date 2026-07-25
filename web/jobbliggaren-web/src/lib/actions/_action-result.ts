/**
 * Shared return contract for form/mutation server actions (#612). The identical
 * `export type ActionResult` was previously re-declared in
 * `applications`/`me`/`match-preferences`/`resumes`; it now lives here and
 * **consumers import it from this module directly**.
 *
 * Those four modules used to re-export it (`export type { ActionResult }`) so their
 * existing consumers did not have to change. That is retired and must not come back:
 * a `"use server"` file may only export async functions — Next says so itself
 * (`next-flight-loader/action-validate.js`, error `E352`). Webpack only checked it at
 * runtime, so the re-export survived three weeks unnoticed; Turbopack checks at module
 * link and every page reaching such a module returned 500 (#1059).
 *
 * Payload-carrying and domain-named variants (e.g. `CreateApplicationFromJobAdResult`,
 * `SaveJobAdResult`, `CvSuggestResult`) stay local to their action file — they are
 * distinct contracts, not this same knowledge piece.
 */
export type ActionResult =
  | { success: true }
  | { success: false; error: string };
