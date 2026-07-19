import type { JobAdDetailDto } from "@/lib/dto/job-ads";
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
//
// #745 — `<JobAdDetail>` renderar annonstexten (`description`), men den ligger
// inte längre på LIST-typen `JobAdDto` (som tappade fältet). Adaptern producerar
// därför detalj-formen minus `contacts` (`Omit<JobAdDetailDto, "contacts">` —
// gäst-demon fabricerar aldrig en rekryterarkontakt, så contacts-blocket utelämnas
// och self-hider). Namnet speglar returtypen (§5): `toJobAdDetail`, ej `toJobAdDto`.

export function toJobAdDetail(
  mock: GuestMockJobAd,
): Omit<JobAdDetailDto, "contacts"> {
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
