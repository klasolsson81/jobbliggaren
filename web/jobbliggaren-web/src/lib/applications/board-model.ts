import { PIPELINE_ORDER } from "@/lib/applications/status";
import { applicationMatchesQuery } from "@/lib/applications/search";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";

/**
 * Tavla-boardets rena modell (#630 PR 8, ADR 0092 D2) — de sökfiltrerade korten
 * grupperade per status. Pure functions så den optimistiska flytten är testbar
 * utan DOM/DnD (CLAUDE.md §2.4). Alla 10 statusar finns alltid som nyckel
 * (aktiva → 6 kolumner, terminala → 4 mini-zoner) så griden aldrig kraschar på
 * en gles svarsform.
 */
export type BoardBuckets = Record<ApplicationStatus, ApplicationDto[]>;

/** En optimistisk flytt: kort `id` → målstatus `to`. */
export interface BoardMove {
  id: string;
  to: ApplicationStatus;
}

function emptyBuckets(): BoardBuckets {
  const buckets = {} as BoardBuckets;
  for (const status of PIPELINE_ORDER) buckets[status] = [];
  return buckets;
}

/**
 * Bas-modell ur pipelinen: sökfiltrerade kort per status. Bevarar backend-
 * ordningen inom varje grupp. Defensiv mot en okänd status i svaret.
 */
export function bucketsFromGroups(
  groups: PipelineGroupDto[],
  trimmedLowerQuery: string,
): BoardBuckets {
  const buckets = emptyBuckets();
  for (const group of groups) {
    if (!(group.status in buckets)) continue;
    buckets[group.status] = group.applications.filter((application) =>
      applicationMatchesQuery(application, trimmedLowerQuery),
    );
  }
  return buckets;
}

/**
 * Optimistisk flytt (ADR 0092 Livscykel-amendment 2026-07-06, board-scoped):
 * flytta kortet till målkolumnen OCH stämpla dess status till `to`, så kolumn +
 * kort-status är konsistenta under den transienta bryggan tills servern
 * (revalidatePath) rekoncilierar. Söker `id` över ALLA bucketar (robust mot en
 * stapel väntande flyttar där drag-tidens status redan är inaktuell).
 *
 * Attention/urgens och tidsderivat (`attentionSignal`, `lastStatusChangeAt`)
 * rörs ALDRIG här — de förblir server-data tills revalidate (CTO-bind D-C: den
 * optimistiska ytan flyttar kolumnplacering, aldrig klient-omräknad server-logik;
 * exakt den divergens PR 7:s icke-optimistiska bind skyddade mot).
 */
export function applyBoardMove(
  buckets: BoardBuckets,
  move: BoardMove,
): BoardBuckets {
  let moved: ApplicationDto | undefined;
  const next = emptyBuckets();
  for (const status of PIPELINE_ORDER) {
    for (const application of buckets[status] ?? []) {
      if (application.id === move.id) {
        moved = application;
        continue;
      }
      next[status].push(application);
    }
  }
  if (moved != null) {
    next[move.to].unshift({ ...moved, status: move.to });
  }
  return next;
}
