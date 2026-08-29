import type { JobAdDetailDto } from "@/lib/dto/job-ads";
import type {
  ApplicationDto,
  ApplicationStatus,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import type { ListCompanyWatchesResult } from "@/lib/dto/company-follows";
import type {
  GuestApplicationStatus,
  GuestMockApplication,
  GuestMockCompanyWatch,
  GuestMockJobAd,
  GuestPipelineGroup,
} from "./mock-data";

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

// #1572 — /gast/oversikt komponeras numera på appens `ApplicationSummary` och
// `CompanySummary`, som båda läser BE-DTO:er. Adaptrarna nedan är den enda vägen
// dit, per filens doktrin ovan.

/**
 * Gästens fem demo-statusar → appens tio riktiga. `Interview`/`Offer` finns inte
 * i `ApplicationStatus`; `InterviewScheduled` respektive `OfferReceived` är de
 * som mockens egen text beskriver ("har bekräftat intervjutid", "väntar svar").
 *
 * `Record<GuestApplicationStatus, …>` gör mappningen TOTAL: en ny gäststatus utan
 * rad blir ett kompileringsfel i stället för en tyst `undefined` som skulle
 * passera zod-fritt hela vägen ut i sammanfattningen.
 */
const STATUS_TO_APPLICATION_STATUS: Record<
  GuestApplicationStatus,
  ApplicationStatus
> = {
  Draft: "Draft",
  Submitted: "Submitted",
  Interview: "InterviewScheduled",
  Offer: "OfferReceived",
  Rejected: "Rejected",
};

function toApplicationDto(mock: GuestMockApplication): ApplicationDto {
  return {
    id: mock.id,
    // Demot har ingen inloggad användare. Ett stabilt literal-id håller formen hel
    // utan att fabricera en identitet: fältet når aldrig en renderad yta.
    jobSeekerId: "guest-demo",
    // `null` = ingen annonsrad alls (manuell), vilket är den mindre utsagan. Gästens
    // ansökningsmock bär inget annons-id att peka på.
    jobAdId: null,
    status: STATUS_TO_APPLICATION_STATUS[mock.status],
    createdAt: mock.updatedAtIso,
    updatedAt: mock.updatedAtIso,
  };
}

/**
 * Gäst-pipelinen som `PipelineGroupDto[]` för `<ApplicationSummary>`.
 *
 * Grupperna materialiseras HELA — `count === applications.length` per grupp —
 * fastän sammanfattningen bara läser `status` och `count`. En grupp med ett tal
 * och en tom lista hade varit en fixtur som motsäger sig själv, och den
 * likheten är dessutom det som gör adaptern testbar.
 */
export function toPipelineGroups(
  groups: ReadonlyArray<GuestPipelineGroup>,
): PipelineGroupDto[] {
  return groups.map((g) => ({
    status: STATUS_TO_APPLICATION_STATUS[g.status],
    count: g.count,
    applications: g.applications.map(toApplicationDto),
  }));
}

/** Bevakade företag som `CompanyWatch[]` för `<CompanySummary>`. */
export function toCompanyWatches(
  watches: ReadonlyArray<GuestMockCompanyWatch>,
): ListCompanyWatchesResult {
  return watches.map((w) => ({
    id: w.id,
    organizationNumber: w.organizationNumber,
    isProtectedIdentity: false,
    companyName: w.companyName,
    followedAt: w.followedAt,
    activeAdCount: w.activeAdCount,
    matchingAdCount: w.matchingAdCount,
    // Ett filter med noll val: dto:ns `null` betyder "inget filter", och raden
    // "N bevakningar har notisfilter" ska kunna renderas i demot.
    filter: w.hasFilter
      ? { municipalities: [], regions: [], onlyMatched: true, remote: false }
      : null,
  }));
}
