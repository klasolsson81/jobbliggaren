import { codeInputSchema } from "@/lib/auth/challenge-schemas";
import { checkNewAddress } from "@/lib/auth/new-address";

// `/adressbyte`, where an account's owner completes the address change an administrator started (#1975, ADR 0153):
// its fields, the check they get before anything is sent, and the state its Server Action answers. A module of its
// own: the action is "use server" and may export only async functions (`_action-result.ts`, #1059), and the form
// runs the same check in the browser.

export type AddressChangeField = "currentEmail" | "newEmail" | "code";

/** Why a field was refused before anything was sent; each has its own copy. */
export type AddressChangeFieldRefusal = "required" | "invalid" | "same" | "malformed";

export type AddressChangeFieldErrors = Readonly<Partial<Record<AddressChangeField, AddressChangeFieldRefusal>>>;

export interface AddressChangeInput {
  readonly currentEmail: string;
  readonly newEmail: string;
  readonly code: string;
}

/** The addresses the action echoes, so the form can re-seed them after React resets it. Never the code. */
export interface AddressChangeValues {
  readonly currentEmail: string;
  readonly newEmail: string;
}

export type AddressChangeCheck =
  | { readonly ok: true; readonly input: AddressChangeInput }
  | { readonly ok: false; readonly errors: AddressChangeFieldErrors };

/**
 * The fields before anything is sent, the same in the browser and in the action: each address as any address must
 * be (`checkNewAddress`'s rule, which an empty address to differ from leaves at that), two that differ, and a code of
 * six digits. What goes over the wire is the two addresses trimmed and the code; whether they match a change is the
 * backend's alone, in login's fold, and its answer never says which input did not.
 */
export function checkAddressChange(typed: AddressChangeInput): AddressChangeCheck {
  const errors: { [K in AddressChangeField]?: AddressChangeFieldRefusal } = {};
  const current = checkNewAddress(typed.currentEmail, "");
  if (!current.ok) errors.currentEmail = current.reason;
  const next = checkNewAddress(typed.newEmail, current.ok ? current.address : "");
  if (!next.ok) errors.newEmail = next.reason;
  const code = codeInputSchema.safeParse(typed.code);
  if (!code.success) errors.code = typed.code.trim() === "" ? "required" : "malformed";

  if (!current.ok || !next.ok || !code.success) return { ok: false, errors };
  return { ok: true, input: { currentEmail: current.address, newEmail: next.address, code: code.data } };
}

/** What the action answers; the form words it, so nothing here is copy. */
export type AddressChangeState =
  | { readonly kind: "invalid"; readonly errors: AddressChangeFieldErrors; readonly values: AddressChangeValues }
  /** Every refusal, one answer: nothing tells which input was wrong (ADR 0153). */
  | { readonly kind: "refused"; readonly values: AddressChangeValues }
  /** A full match before the delay has run; nothing was spent. `completableFrom` is an ISO instant. */
  | { readonly kind: "notYet"; readonly completableFrom: string; readonly values: AddressChangeValues }
  | { readonly kind: "tooManyAttempts"; readonly values: AddressChangeValues }
  /** Decided before any input was read, so trying again later is true. */
  | { readonly kind: "unavailable"; readonly values: AddressChangeValues }
  /** Nothing readable came back: the address may have changed, so this claims nothing and advises no retry. */
  | { readonly kind: "unknown"; readonly values: AddressChangeValues }
  | { readonly kind: "done" }
  | null;
