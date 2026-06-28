import type { JobAdDto } from "@/lib/dto/job-ads";
import type { GuestMockJobAd } from "./mock-data";

// F-Pre Punkt 5b 2026-05-24 — adapters för att map:a gäst-mockdata till
// DTO-shapes så befintliga presentational-komponenter (`<JobAdDetail>`)
// kan återanvändas utan dual-shape-bloat (CTO Beslut 6).
//
// Gäst-tree konsumerar BE-shape ENDAST via dessa adapters — ingen riktig
// BE-anrop sker. Adapter-funktionerna är pure + sync + utan side effects.
//
// NY-taggen (#293/#306): den tidsbaserade `isNew`-flaggan är borttagen ur
// JobAdDto. NY = OLÄST kräver en per-användar watermark (auth) — en anonym
// gäst har ingen ⇒ ingen NY (W4 cold-start). Gäst-demon behåller
// "X DAGAR"-färskheten som recency-signal.

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
  };
}
