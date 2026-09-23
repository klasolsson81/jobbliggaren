import { comparableAddress } from "@/lib/auth/comparable-address";

/** Why a new address was refused before any code was spent on it; each reason has its own copy. */
export type NewAddressRefusal = "required" | "invalid" | "same";

export type NewAddressVerdict =
  | { ok: true; address: string }
  | { ok: false; reason: NewAddressRefusal };

/** The `settings` message of each refusal: the card shows it on the field, the action as a status. */
export const NEW_ADDRESS_REFUSAL_COPY = {
  required: "account.changeEmail.newEmailRequired",
  invalid: "account.changeEmail.invalidEmail",
  same: "account.changeEmail.sameEmail",
} as const satisfies Record<NewAddressRefusal, string>;

// Characters `StorableAddress` refuses in the handler, after a code is spent: control, whitespace,
// surrogate and format characters.
const FORBIDDEN = /[\s\p{Cc}\p{Cf}\p{Cs}]/u;

const hasSurrogateUnit = (value: string) =>
  [...Array(value.length).keys()].some((i) => {
    const unit = value.charCodeAt(i);
    return unit >= 0xd800 && unit <= 0xdfff;
  });

/**
 * Checks a new address before a code is spent on it (security-auditor, #1740 Minor 4). It refuses at
 * least what the backend's validation step refuses — empty, over 256, not exactly one "@" that is
 * neither first nor last (`ChangeEmailCommandValidator`) — plus the characters `StorableAddress`
 * refuses, and never an address the backend would take. The trimmed value is the one that is sent,
 * verbatim, to both change-email calls: the grant binds the address as the request spelled it.
 */
export function checkNewAddress(input: string, currentEmail: string): NewAddressVerdict {
  const address = input.trim();
  if (address.length === 0) return { ok: false, reason: "required" };

  const at = address.indexOf("@");
  if (
    address.length > 256 ||
    at <= 0 ||
    at === address.length - 1 ||
    at !== address.lastIndexOf("@") ||
    FORBIDDEN.test(address) ||
    hasSurrogateUnit(address)
  ) {
    return { ok: false, reason: "invalid" };
  }

  if (comparableAddress(address) === comparableAddress(currentEmail)) {
    return { ok: false, reason: "same" };
  }
  return { ok: true, address };
}
