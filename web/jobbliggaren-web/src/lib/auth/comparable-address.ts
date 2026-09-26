/**
 * The one form two addresses are compared in on this side of the wire: trimmed, Unicode-normalised to
 * NFC, lower case (security-auditor, #1740 Minor 4). The stored address is exactly what was typed, so
 * the same address can arrive in two normal forms; a comparison that only folds case would then tell
 * a user that her own address is not hers, and lock her out of deleting her account.
 *
 * It decides only whether two spellings are the same. What an address IS stays the backend's
 * (`StorableAddress`), and nothing is sent in this form: a value goes over the wire as it was typed.
 */
export function comparableAddress(address: string): string {
  return address.trim().normalize("NFC").toLowerCase();
}
