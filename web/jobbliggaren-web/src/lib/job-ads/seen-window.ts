/**
 * #759 (syskon till #477 Low 4) — den sedda /jobb-sidans "fönster-max".
 *
 * Vattenmärket (`markJobsSeen`) ska flyttas fram till den max:a `createdAt` bland
 * de annonser användaren FAKTISKT renderade — INTE `items[0]`. Till skillnad från
 * den nyast-först-sorterade /matchningar-listan kan /jobb vara relevans-/matchrank-
 * sorterad, så det nyaste elementet ligger inte nödvändigtvis först. Att ta `items[0]`
 * skulle på en relevanssorterad sida sätta vattenmärket för lågt eller för högt och
 * återinföra exakt #759-buggen.
 *
 * Ren funktion (testbar utan RSC): returnerar den ORIGINALA ISO-strängen för det
 * element vars parsade tidsstämpel är störst (full precision — ingen millisekund-
 * truncation via `Date.parse`, paritet `markMatchesSeen`). Oparsbara `createdAt`
 * hoppas över. Tom lista, eller en lista där INGET `createdAt` är parsbart, → `undefined`
 * (kallaren skickar då ingen body och backend faller tillbaka på klock-nu).
 */
export function maxCreatedAt(
  items: ReadonlyArray<{ createdAt: string }>,
): string | undefined {
  let max: string | undefined;
  let maxMs = Number.NEGATIVE_INFINITY;
  for (const it of items) {
    const ms = Date.parse(it.createdAt);
    if (!Number.isNaN(ms) && ms > maxMs) {
      maxMs = ms;
      max = it.createdAt;
    }
  }
  return max;
}
