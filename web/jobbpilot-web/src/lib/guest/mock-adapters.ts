import type { JobAdDto } from "@/lib/dto/job-ads";
import type { GuestMockJobAd } from "./mock-data";

// F-Pre Punkt 5b 2026-05-24 — adapters för att map:a gäst-mockdata till
// DTO-shapes så befintliga presentational-komponenter (`<JobAdDetail>`)
// kan återanvändas utan dual-shape-bloat (CTO Beslut 6).
//
// Gäst-tree konsumerar BE-shape ENDAST via dessa adapters — ingen riktig
// BE-anrop sker. Adapter-funktionerna är pure + sync + utan side effects.

export function toJobAdDto(mock: GuestMockJobAd): JobAdDto {
  return {
    id: mock.id,
    title: mock.title,
    companyName: mock.companyName,
    description: mock.description,
    url: mock.url,
    source: mock.source,
    status: "Active",
    publishedAt: mock.publishedAtIso,
    expiresAt: mock.expiresAtIso,
    createdAt: mock.publishedAtIso,
    // isNew = publicerad senaste 7 dagarna mot referensdatum 2026-05-24.
    isNew: isWithinDays(mock.publishedAtIso, 7),
  };
}

function isWithinDays(iso: string, days: number): boolean {
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return false;
  const ms = days * 24 * 60 * 60 * 1000;
  // Använd referens-stämpel istället för Date.now() så vitest-snapshots inte
  // driftar — gäst-mockdata är frozen ändå.
  const REF_NOW = Date.parse("2026-05-24T20:00:00Z");
  return REF_NOW - t <= ms;
}
