import { ACTIVE_PIPELINE_STATUSES, PIPELINE_ORDER } from "./status";
import type {
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

/**
 * Antal per status ur pipelinens grupper. Delad av stegrailen (/ansokningar) och
 * sammanfattningen (/oversikt) så de två ytorna aldrig kan säga olika saker om
 * samma konto (CLAUDE.md §9.1 DRY).
 *
 * ⚠ Backend grupperar med GroupBy(Status) och utelämnar därför tomma statusar
 * HELT — en status utan ansökningar har ingen grupp, inte en grupp med noll.
 * Läs alltid via statusCount, aldrig med en direkt Map-uppslagning.
 *
 * Antalet läses ur group.count (backend-sanning) och aldrig ur
 * group.applications.length, som kan vara trimmad.
 */
export type PipelineCounts = ReadonlyMap<ApplicationStatus, number>;

export function countByStatus(
  groups: ReadonlyArray<PipelineGroupDto>,
): PipelineCounts {
  return new Map(groups.map((g) => [g.status, g.count] as const));
}

export function statusCount(
  counts: PipelineCounts,
  status: ApplicationStatus,
): number {
  return counts.get(status) ?? 0;
}

/** Summa över alla tio statusar — samma storhet som tavlans count. */
export function totalCount(counts: PipelineCounts): number {
  return PIPELINE_ORDER.reduce((sum, s) => sum + statusCount(counts, s), 0);
}

/**
 * Summa över de sex icke-terminala stegen — samma storhet som tavlans active,
 * eftersom båda läser ACTIVE_PIPELINE_STATUSES. Det är den enda partitionen;
 * repot hade tidigare en andra, avvikande definition av "aktiv" i
 * oversikt/aggregations.ts som inkluderade Ghosted. Den är borta.
 */
export function activeCount(counts: PipelineCounts): number {
  return ACTIVE_PIPELINE_STATUSES.reduce(
    (sum, s) => sum + statusCount(counts, s),
    0,
  );
}
