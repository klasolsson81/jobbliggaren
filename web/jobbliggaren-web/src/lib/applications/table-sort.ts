import { daysInStatus } from "./urgency";
import { PIPELINE_ORDER } from "./status";
import type { ApplicationDto } from "@/lib/dto/applications";

/** Tabell-vyns sorterbara kolumner (design §7): Roll & företag, Status, I steget. */
export type TableSortKey = "role" | "status" | "days";
export type SortDir = "asc" | "desc";

const STATUS_RANK: ReadonlyMap<string, number> = new Map(
  PIPELINE_ORDER.map((status, index) => [status, index] as const),
);

// "Roll & företag" sorteras på synlig titel (roll). En rad utan kopplad annons
// (manuell ansökan utan titel) sorteras som tom sträng → hamnar först i stigande.
function roleKey(application: ApplicationDto): string {
  return application.jobAd?.title ?? "";
}

/**
 * Ren komparator för Tabell-vyns sorterbara kolumner (#630 PR 10). Extraherad ur
 * komponenten så sorteringen är enhetstestbar med injicerat `now` — date-flake-
 * fri (reference_oversikt_test_dayofmonth_flake). Default = "days" desc (längst
 * väntan överst, design §7). `null`-dagar (saknad `lastStatusChangeAt`) sorteras
 * ALLTID sist, oavsett riktning — en rad utan data sjunker, aldrig ett fabricerat
 * 0 (§5). Anropas via `[...rows].sort(...)` (kopia — muterar inte källan).
 */
export function compareApplications(
  a: ApplicationDto,
  b: ApplicationDto,
  sortKey: TableSortKey,
  sortDir: SortDir,
  now: Date,
): number {
  const dir = sortDir === "asc" ? 1 : -1;

  switch (sortKey) {
    case "role":
      return roleKey(a).localeCompare(roleKey(b), "sv") * dir;
    case "status": {
      const ra = STATUS_RANK.get(a.status) ?? Number.MAX_SAFE_INTEGER;
      const rb = STATUS_RANK.get(b.status) ?? Number.MAX_SAFE_INTEGER;
      return (ra - rb) * dir;
    }
    case "days": {
      const da = daysInStatus(a.lastStatusChangeAt, now);
      const db = daysInStatus(b.lastStatusChangeAt, now);
      if (da == null && db == null) return 0;
      if (da == null) return 1; // null sist oavsett riktning
      if (db == null) return -1;
      return (da - db) * dir;
    }
  }
}
