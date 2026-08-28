import type {
  ApplicationDto,
  PipelineGroupDto,
} from "@/lib/dto/applications";
import type { ListSavedJobAdsResult } from "@/lib/dto/saved-job-ads";
import { formatTime, type JpFormatter } from "@/lib/i18n/format";
import { daysSince } from "@/lib/i18n/relative-time";

// Relative-time helpers live in `lib/i18n/relative-time` now (#336 DRY
// extraction). Re-exported here so existing oversikt consumers keep their
// `@/lib/oversikt/aggregations` import path; new code imports from the i18n
// module directly.
export {
  daysSince,
  formatDaysAgo,
  type RelativeTimeTranslator,
} from "@/lib/i18n/relative-time";

/**
 * F6 P5 Punkt 4 — Översikt-aggregeringar.
 *
 * Pure helpers — testbara utan request-kontext. Inga date-FNS/Intl-tunga
 * dependencies; svensk lokal-formatering är kort nog att handrullas och
 * speglar CLAUDE.md §10.2 (datum "14 apr 2026", tid 24h).
 */

/**
 * Samlar alla ansökningar från pipeline-grupper i en platt array. Behövs
 * för datum-filter (uppföljnings-fönstret {@link OVERSIKT_FOLLOW_UP_DAYS},
 * Intervju <1d) som inte kan beräknas från counts alone.
 */
export function flattenPipeline(
  pipeline: ReadonlyArray<PipelineGroupDto>
): ReadonlyArray<ApplicationDto> {
  const out: ApplicationDto[] = [];
  for (const group of pipeline) {
    for (const app of group.applications) out.push(app);
  }
  return out;
}

const SV_WEEKDAYS = [
  "söndag",
  "måndag",
  "tisdag",
  "onsdag",
  "torsdag",
  "fredag",
  "lördag",
];

const SV_MONTHS_LONG = [
  "januari",
  "februari",
  "mars",
  "april",
  "maj",
  "juni",
  "juli",
  "augusti",
  "september",
  "oktober",
  "november",
  "december",
];

const SV_MONTHS_SHORT = [
  "jan",
  "feb",
  "mar",
  "apr",
  "maj",
  "jun",
  "jul",
  "aug",
  "sep",
  "okt",
  "nov",
  "dec",
];

/**
 * Svensk kortform "13 maj" (CLAUDE.md §10.2 — "14 apr 2026" eller "13 maj").
 * Returnerar "–" vid ogiltig input istället för att kasta.
 *
 * Lokal kalenderdag-trunkering: använder klientens lokala tidszon (server
 * körs UTC men UI:t serverrenderas och hydrerar identiskt — datum-strings
 * från BE är ISO och Date-parsade konsistent).
 */
export function formatSwedishShortDate(isoString: string): string {
  const d = new Date(isoString);
  if (Number.isNaN(d.getTime())) return "–";
  return `${d.getDate()} ${SV_MONTHS_SHORT[d.getMonth()]}`;
}

/**
 * Svensk kortform MED år ("14 jun 2026", CLAUDE.md §10.2). Använd där posterna
 * ackumuleras över tid och året bär betydelse (t.ex. "Mina matchningar"-vyn,
 * ADR 0080) — till skillnad från `formatSwedishShortDate` som utelämnar året för
 * kompakt, samma-säsong-kontext. Återanvänder samma `SV_MONTHS_SHORT` så formerna
 * aldrig driftar isär. Returnerar "–" vid ogiltig input i stället för att kasta.
 */
export function formatSwedishShortDateWithYear(isoString: string): string {
  const d = new Date(isoString);
  if (Number.isNaN(d.getTime())) return "–";
  return `${d.getDate()} ${SV_MONTHS_SHORT[d.getMonth()]} ${d.getFullYear()}`;
}

export interface SwedishLongDate {
  readonly day: number;
  readonly weekday: string;
  readonly monthYear: string;
}

/**
 * Lång svensk form för "Idag"-kortets datumblock:
 * { day: 23, weekday: "lördag", monthYear: "maj 2026" }
 */
export function formatSwedishLongDate(date: Date): SwedishLongDate {
  return {
    day: date.getDate(),
    weekday: SV_WEEKDAYS[date.getDay()] ?? "",
    monthYear: `${SV_MONTHS_LONG[date.getMonth()] ?? ""} ${date.getFullYear()}`,
  };
}

/**
 * Uppföljnings-fönstret för Översikt-notisen, i dagar (#384). Exporterad som EN
 * SSOT: filtret nedan OCH copy-talet (`notices.followUpText` via en ICU
 * `{days}`-param) läser samma konstant, så tröskeln och det visade talet aldrig
 * kan drifta isär (drift-guard-mönstret från #291). Detta är FE-side view-policy
 * (parallellt med `findRecentInterviews` ≤1d / `findUpcomingSavedJobDeadlines`),
 * MEDVETET SKILT från /ansokningar-attentionens design §11-trösklar (no-response
 * nudge 14d, ghost-förslag 30d) — Översikten är en lättare nudge-yta (CTO-dom #384).
 */
export const OVERSIKT_FOLLOW_UP_DAYS = 14;

/**
 * Returnerar ansökningar som behöver uppföljning: status ∈ {Submitted,
 * Acknowledged} och `appliedAt` (datumet ansökan SKICKADES) ligger mer än
 * {@link OVERSIKT_FOLLOW_UP_DAYS} dagar sedan.
 *
 * #384: ankras i `appliedAt`, INTE `createdAt`. "Inte fått svar på X dagar" mäts
 * från när arbetsgivaren fick ansökan, inte när användaren skapade ett utkast i
 * sitt eget verktyg. Ett utkast skapat 2026-06-11 men skickat 2026-06-28 har
 * väntat 1 dag på svar, inte 18 — samma datum-SSOT (`appliedAt`) som "skickad i
 * går" på Mina ansökningar. `appliedAt` är nullable i DTO:n; en Submitted/
 * Acknowledged-ansökan har alltid ett (domänen stämplar det vid Submitted-
 * övergången), men null-guarden gör helpern defensiv — inget apply-datum ⇒ inget
 * ankare ⇒ ingen kandidat (paritet BE `ApplicationAttentionEvaluator`).
 *
 * Driver Uppföljning-notisen. Tom array ⇒ dölj notisen helt (HANDOVER §3.3).
 */
export function findFollowUpCandidates(
  apps: ReadonlyArray<ApplicationDto>,
  now: Date = new Date()
): ReadonlyArray<ApplicationDto> {
  return apps.filter(
    (a) =>
      (a.status === "Submitted" || a.status === "Acknowledged") &&
      a.appliedAt != null &&
      daysSince(a.appliedAt, now) > OVERSIKT_FOLLOW_UP_DAYS
  );
}

/**
 * Formaterar notis-panelens "senast uppdaterad"-stämpel som `HH:mm` i
 * LÄSARENS tidszon. Översikt-sidan är `force-dynamic` och beräknar notiserna LIVE per
 * request, så render-tiden ÄR den ärliga tidpunkten.
 *
 * Den skrev tidigare UTC:s väggklocka och presenterade den som lokal tid, så stämpeln låg
 * alltid två timmar efter en svensk läsares klocka under CEST — omöjligt att skilja från en
 * frusen och gammal siffra (#1549). Motiveringen som stod här var att UTC var konsekvent
 * med `daysSince`-trunkeringen, men de två är olika saker: `daysSince` räknar
 * kalenderdagars SKILLNAD och trunkerar i UTC för DST-stabilitet, medan den här stämpeln är
 * en absolut väggklocka som läsaren jämför med sin egen. Tidszonen ägs nu av next-intls
 * formaterare, som resten av appen (AGENTS.md §10).
 *
 * Datumdelen utgick med #1556: den bär information bara det dygn en flik står öppen över
 * midnatt, och raden konkurrerade med sidans innehåll om bredden. Toolbaren behåller hela
 * tidpunkten i ett `<time dateTime>`, så inget går förlorat i DOM:en. Formen delas med
 * `formatTime` i stället för att komponeras om här — den är husets 24-timmarsform
 * (AGENTS.md §10) och samma tidszonsauktoritet, och två hem för den driftar isär.
 * Returnerar "–" vid ogiltig input i stället för att kasta.
 */
export function formatNoticesStamp(format: JpFormatter, date: Date): string {
  if (Number.isNaN(date.getTime())) return "–";
  return formatTime(format, date);
}

/**
 * Returnerar nyligen bekräftade intervjuer: status === InterviewScheduled
 * och `updatedAt` ligger inom 1 UTC-kalenderdag bakåt från `now` (kan i
 * praktiken vara upp till ~47h gammal pga `daysSince`-trunkering). Driver
 * Intervju-bekräftelse-notisen — fönstret är kalenderdag-bundet, inte
 * 24h rullande, för att matcha "igår"/"idag"-copyn.
 */
export function findRecentInterviews(
  apps: ReadonlyArray<ApplicationDto>,
  now: Date = new Date()
): ReadonlyArray<ApplicationDto> {
  return apps.filter(
    (a) =>
      a.status === "InterviewScheduled" && daysSince(a.updatedAt, now) <= 1
  );
}

/**
 * Returnerar nyaste erbjudandet (OfferReceived) — sorterat på updatedAt desc.
 * `null` om inga finns. Driver Erbjudande-notisen.
 */
export function findLatestOffer(
  apps: ReadonlyArray<ApplicationDto>
): ApplicationDto | null {
  const offers = apps
    .filter((a) => a.status === "OfferReceived")
    .slice()
    .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  return offers[0] ?? null;
}

/**
 * Deadline-fönstret för Översikt-notisen, i dagar (#726 SSOT). Copyn läser samma
 * konstant via en ICU `{days}`-param — det visade talet och filtrets fönster kan
 * aldrig drifta isär (drift-guard-mönstret från #291/#384). En sparad annons vars
 * `expiresAt` ligger idag t.o.m. {@link OVERSIKT_DEADLINE_WINDOW_DAYS} dagar fram
 * räknas med.
 */
export const OVERSIKT_DEADLINE_WINDOW_DAYS = 7;

export interface OversiktSavedJobDeadline {
  readonly company: string;
  /** Annonsens sista ansökningsdag (ISO), råstämpel för formatering i UI. */
  readonly expiresAt: string;
}

/**
 * Returnerar sparade annonser vars sista ansökningsdag (`jobAd.expiresAt`) ligger
 * idag eller i framtiden inom {@link OVERSIKT_DEADLINE_WINDOW_DAYS} dagar. Ersätter
 * den tidigare mock-drivna deadline-notisen (#726): riktig `expiresAt` ur
 * `ListSavedJobAdsResult` i stället för `OVERSIKT_MOCK.savedJobsDeadlines`.
 *
 * `daysSince(expiresAt, now)` ger NEGATIVT för framtida datum, så fönstervillkoret
 * är `<= 0` (idag eller framåt) OCH `>= -WINDOW` (inte längre bort än fönstret).
 * Passerade deadlines (positivt diff) och rader utan `jobAd`/`expiresAt` faller
 * bort. Sorteras stigande på `expiresAt` (närmast först). Tom array ⇒ dölj notisen.
 */
export function findUpcomingSavedJobDeadlines(
  savedJobAds: ListSavedJobAdsResult,
  now: Date = new Date()
): ReadonlyArray<OversiktSavedJobDeadline> {
  const entries: OversiktSavedJobDeadline[] = [];
  for (const saved of savedJobAds) {
    const jobAd = saved.jobAd;
    if (jobAd == null || jobAd.expiresAt == null) continue;
    // Defensiv: en ogiltig stämpel skulle annars ge daysSince → 0 (= "idag") och
    // smyga in i fönstret. Backend skickar giltig ISO, men guardas ändå.
    if (Number.isNaN(new Date(jobAd.expiresAt).getTime())) continue;
    const diff = daysSince(jobAd.expiresAt, now);
    if (diff <= 0 && diff >= -OVERSIKT_DEADLINE_WINDOW_DAYS) {
      entries.push({ company: jobAd.company, expiresAt: jobAd.expiresAt });
    }
  }
  entries.sort((a, b) => a.expiresAt.localeCompare(b.expiresAt));
  return entries;
}

