/**
 * Removes comments from TypeScript/TSX source, for static guards that ask what a file
 * RENDERS rather than what it says about itself.
 *
 * It exists because `v3-native-routes.test.ts` decides whether a page owns a width container by
 * looking for class names in a source file, and the first revision looked at the raw text. That
 * made the guard **fail-open on the very file it was written for** (#1062): `cv/[id]/granska`
 * had just gained a docblock naming `jp-pagehero` and `jp-container jp-page` in prose, so the
 * match was satisfied by the comment — with every container class deleted from its markup, the
 * guard still reported green.
 *
 * ⚠ **It deliberately does NOT track quote state, and that absence is the fix for a SECOND
 * fail-open the first repair shipped.** Tracking quotes means an apostrophe in JSX text
 * (`<p>Klas' CV</p>`), in a regex literal (`/don't/`), or a stray inch mark (`5"`) opens a
 * string state that never closes — and from there the rest of the file reads as one long
 * string, comments stop being stripped, and the original hole is back by a new mechanism.
 * Removing quote tracking removes the whole class.
 *
 * The only thing quote tracking bought was not mistaking the `//` in `href="https://…"` for a
 * line comment. The URL check below buys that back — and it is **anchored, not a whitespace
 * walk**, which is the difference between a rule and a third fail-open.
 *
 * ⚠ **A whitespace-walking colon check was tried and measured fail-open on six shapes** (#1062,
 * code-reviewer M-1): `case "a": //…`, `default: //…`, `{ shell: //…`, `a ? b : //…`,
 * `const x: //…`, `outer: //…` are all genuine line comments whose `//` follows a colon, and
 * every one of them survived — so a class named in such a comment satisfied the guard again.
 * The anchor closes all six, measured: the colon must **abut** the slashes and itself follow a
 * URL-scheme character, which a label colon separated by a space never does.
 *
 * ⚠ **The residual, stated because a guard that hides its own reach is worse than a narrow
 * one**: `default://jp-container` — a label colon with no space before the slashes — is still
 * kept. No such shape exists in the corpus and nobody writes one, but it is a hole, not a proof.
 *
 * Errors bias **fail-closed everywhere except that one branch**: mis-reading a comment drops
 * text and makes the guard stricter, while the URL branch is a *keep* branch, so its mistakes
 * keep text. That asymmetry is why the keep branch is the narrow one. Both directions are
 * pinned in `strip-comments.test.ts` — this function is a guard's oracle, and an oracle whose
 * keep branch is untested is not one.
 *
 * Not a parser, and not trying to be: it answers "is this identifier outside a comment", which
 * is all a class-name sweep needs.
 */
/** The character class a URI scheme is built from (RFC 3986 §3.1), minus the leading letter. */
const SCHEME_CHAR = /[a-z0-9+.-]/i;

export function stripComments(source: string): string {
  const NEWLINE = "\n";
  let out = "";
  for (let i = 0; i < source.length; i++) {
    if (source[i] === "/" && source[i + 1] === "*") {
      const end = source.indexOf("*/", i + 2);
      i = end === -1 ? source.length : end + 1;
      continue;
    }
    if (source[i] === "/" && source[i + 1] === "/") {
      // A URL scheme ABUTS its slashes (`https://`) and its colon follows a scheme character.
      // A label or type colon is separated from a following comment by whitespace, which is
      // exactly what the earlier walk-back version threw away.
      const isUrlScheme =
        source[i - 1] === ":" && SCHEME_CHAR.test(source[i - 2] ?? "");
      if (isUrlScheme) {
        out += source[i];
        continue;
      }
      const end = source.indexOf(NEWLINE, i);
      i = end === -1 ? source.length : end - 1;
      continue;
    }
    out += source[i];
  }
  return out;
}
