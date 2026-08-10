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
type ActionSuccess = { success: true };
type ActionFailure = { success: false; error: string };

export type ActionResult = ActionSuccess | ActionFailure;

/**
 * `ActionResult` plus an opt-in refusal flag (#734 B-ii). A separate type rather than a
 * wider `ActionResult`, so the many actions and the two `ReAuthDialog` consumers that will
 * never set it do not have to advertise it; the widening is visible in the signature of
 * the one action that does.
 *
 * It is DERIVED from the same two shapes above rather than restated, because two
 * near-identical hand-written unions in one file drift the moment either is edited.
 *
 * `refused` means: refused by deployment configuration, not by the input — no retry with
 * different input can succeed until an operator changes something. It is deliberately
 * named for the class, not the feature.
 *
 * <b>What it is NOT, and the near miss sits in the same function as the only call site:</b>
 * a cooldown or rate-limit is not a refusal. `changeEmailAction`'s 409 arm
 * (`Auth.ChangeEmailCooldown`) is also a refusal by deployment state, but a retry after
 * waiting DOES succeed, so marking it `refused` would tell a cooling-down user their
 * address change is permanently unavailable and remove the control they need.
 *
 * `error` stays populated on the refused variant. A consumer that ignores the flag renders
 * an ordinary error, so the fail-safe is today's behaviour rather than a blank message.
 */
export type RefusableActionResult =
  | ActionSuccess
  | (ActionFailure & { refused?: true });
