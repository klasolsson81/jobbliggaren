import type { ApplicationDto } from "@/lib/dto/applications";

/**
 * Klient-side sök-predikat (ADR 0092 D2 / YAGNI): matchar på roll + företag.
 * SSOT delad av Lista-ön (`applications-pipeline.tsx`) och Tavla-boardet
 * (`board-model.ts`) så de två vyerna aldrig kan drifta isär i vad "sök"
 * betyder (CLAUDE.md §9.1 DRY). En ansökan utan kopplad annons matchar bara tom
 * sökning.
 *
 * `trimmedLowerQuery` förväntas redan trimmad + gemener (kallaren äger
 * normaliseringen en gång, inte per kort).
 */
export function applicationMatchesQuery(
  application: ApplicationDto,
  trimmedLowerQuery: string,
): boolean {
  if (trimmedLowerQuery.length === 0) return true;
  const haystack =
    `${application.jobAd?.title ?? ""} ${application.jobAd?.company ?? ""}`.toLowerCase();
  return haystack.includes(trimmedLowerQuery);
}
