/**
 * Client-side org.nr input normalisers — the FE mirror of the backend `OrganizationNumber` value object.
 * Shared by the unified `/foretag/sok` search island (`ForetagSokSearchbar`), the `/api/foretag/sok` BFF
 * route and the `?namn=` URL gate (`parseNamn`): they decide whether a typed value is an org.nr and
 * whether it is personnummer-shaped (the highest-priority guard). No wire schema lives here — the #454
 * lookup DTO was retired with the lookup surface (#997); the org.nr search result shape is
 * `orgNrSearchResultSchema` in `company-search.ts`.
 */

/**
 * A separator between the digit groups of a written Swedish identity number: ASCII `+`, U+2212 MINUS
 * SIGN, and any `\p{Pd}` (ASCII `-`, U+2013 EN DASH, U+2011 NON-BREAKING HYPHEN — the paste path, since
 * word processors autocorrect and PDFs carry the non-breaking form). That class is the house's own,
 * `Personnummer.IsSeparator` (#497), cited rather than reinvented; whitespace is this side's own
 * long-standing addition and is not part of it. Written with escapes to keep the source ASCII-only,
 * mirroring the C# original.
 */
const SEPARATORS = /[\s\p{Pd}+\u2212]/gu;

/**
 * Strip separators, then accept the domain's WRITTEN-FORM contract: exactly ten digits (the stored
 * form), or twelve digits behind a `19`/`20` century prefix, whose century is stripped. Returns the
 * normalised ten-digit value or null. Mirrors `OrganizationNumber.TryFromWrittenForm`.
 *
 * Parity with the domain is owed on the VALUE axis and not on the PRESENTATION axis (#1075): this
 * derives exactly the value the domain would, and deliberately accepts a wider set of raw strings that
 * reduce to it. The raw string never crosses the wire — callers transmit the derived value — so a wider
 * strip class cannot manufacture a disagreement, while a narrower one would let a written personnummer
 * reach `?namn=`, and with it the address bar, history and the access log (ADR 0087 D8(c); CLAUDE.md §5
 * ranks this guard highest). The domain is a recognizer whose miss falls to a zero-row query; this is a
 * guard whose miss falls into a URL, so where the two postures conflict the guard's direction wins. Do
 * not "restore parity" by narrowing to the domain's single-hyphen-at-length-5 rule.
 *
 * The century strip is why {@link isPersonnummerShapedOrgNr} needs no change: once the century is gone,
 * index 2 holds the month's tens digit, so every real personnummer is refused on the normalised form and
 * no second discriminator is needed. Not widened, deliberately: no century but `19`/`20`, no Luhn or date
 * check (either would narrow the guard), and no Unicode-digit folding — JS `\d` is ASCII-only regardless
 * of the `u` flag, the opposite of .NET, where it means `\p{Nd}` and required #865.
 *
 * The LABEL carries the instruction (no placeholder examples — Klas hard rule), and the field hint stays
 * as written: naming this class would instruct a personnummer entry, and naming the behaviour would
 * advertise the heuristic.
 */
export function normalizeOrgNrInput(raw: string): string | null {
  const stripped = raw.replace(SEPARATORS, "");
  const digits = /^(?:19|20)\d{10}$/.test(stripped) ? stripped.slice(2) : stripped;
  return /^\d{10}$/.test(digits) ? digits : null;
}

/**
 * #454 (ADR 0088 D4) — FE mirror of the backend heuristic `OrganizationNumber.IsPersonnummerShaped()`
 * (a legal-entity org.nr always has 3rd digit >= 2; a personnummer has 0/1). DISPLAY GATE ONLY: the
 * unified field uses it to render the refuse state locally WITHOUT transmitting a potential personnummer
 * anywhere (not even to our own BFF); the backend handler remains the enforcing authority (refuses
 * pre-registry, transmission-fail-closed pinned by arch tests). A #456-sanctioned posture flip updates
 * both sides in one PR. Expects an already-normalised 10-digit value.
 */
export function isPersonnummerShapedOrgNr(orgNr: string): boolean {
  const third = orgNr[2];
  // Fail-safe: the unexpected is sensitive (parity the backend heuristic).
  return !/^\d{10}$/.test(orgNr) || third === undefined || third < "2";
}
