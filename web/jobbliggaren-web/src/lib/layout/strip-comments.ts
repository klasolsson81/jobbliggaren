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
 * line comment, and the colon check below buys that without the failure mode: a line comment's
 * `//` is never preceded by a colon, a URL scheme's always is.
 *
 * Errors bias **fail-closed** — a shape it mis-reads drops text, which makes a guard built on it
 * stricter rather than more permissive. Every claim in this docblock is pinned in
 * `strip-comments.test.ts`; this function is a guard's oracle, and an untested oracle is not one.
 *
 * Not a parser, and not trying to be: it answers "is this identifier outside a comment", which
 * is all a class-name sweep needs.
 */
export function stripComments(source: string): string {
  const NEWLINE = "\n";
  const TAB = "\t";
  let out = "";
  for (let i = 0; i < source.length; i++) {
    if (source[i] === "/" && source[i + 1] === "*") {
      const end = source.indexOf("*/", i + 2);
      i = end === -1 ? source.length : end + 1;
      continue;
    }
    if (source[i] === "/" && source[i + 1] === "/") {
      // Walk back over horizontal whitespace: `https://` has a colon immediately before the
      // slashes; a line comment never does.
      let j = i - 1;
      while (j >= 0 && (source[j] === " " || source[j] === TAB)) j--;
      if (source[j] === ":") {
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
