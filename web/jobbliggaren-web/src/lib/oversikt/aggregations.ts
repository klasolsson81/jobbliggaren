import type {
  ApplicationDto,
  PipelineGroupDto,
} from "@/lib/dto/applications";
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

export interface ApplicationCounts {
  /** status ∉ {Rejected, Withdrawn, Accepted} */
  readonly active: number;
  /** Draft */
  readonly drafts: number;
  /** InterviewScheduled + Interviewing */
  readonly interviews: number;
  /** OfferReceived */
  readonly offers: number;
  /** Rejected */
  readonly rejected: number;
  /** Ghosted */
  readonly ghosted: number;
  /** Submitted (för Uppföljning-notis) */
  readonly submitted: number;
  /** Acknowledged (för Uppföljning-notis) */
  readonly acknowledged: number;
}

const INACTIVE_STATUSES = new Set(["Rejected", "Withdrawn", "Accepted"]);

/**
 * Räknar ansökningar per Översikt-kategori från pipeline-grupperna.
 * `PipelineGroupDto.count` är auktoritativt per status — vi summerar dem
 * istället för att räkna `.applications.length` (groups kan vara trimmade
 * vid stor volym; `count` är total-räknat backend-side per ADR 0048).
 */
export function computeApplicationCounts(
  pipeline: ReadonlyArray<PipelineGroupDto>
): ApplicationCounts {
  const byStatus = new Map<string, number>();
  for (const group of pipeline) {
    byStatus.set(group.status, group.count);
  }
  const get = (s: string): number => byStatus.get(s) ?? 0;

  let active = 0;
  for (const [status, count] of byStatus) {
    if (!INACTIVE_STATUSES.has(status)) active += count;
  }

  return {
    active,
    drafts: get("Draft"),
    interviews: get("InterviewScheduled") + get("Interviewing"),
    offers: get("OfferReceived"),
    rejected: get("Rejected"),
    ghosted: get("Ghosted"),
    submitted: get("Submitted"),
    acknowledged: get("Acknowledged"),
  };
}

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
 * Lång svensk form för "I dag"-kortets datumblock:
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
 * (parallellt med `findRecentInterviews` ≤1d / `filterFutureDeadlines`), MEDVETET
 * SKILT från /ansokningar-attentionens ADR 0085-trösklar (proaktiv nudge 7d,
 * NoResponseLong 21d) — Översikten är en lättare nudge-yta (CTO-dom #384).
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
 * Formaterar notis-panelens "senast uppdaterad"-stämpel som
 * `YYYY-MM-DD · HH:mm` (UTC, konsekvent med sidans övriga UTC-datumhantering —
 * `daysSince`-trunkering, sammanfattnings-stämpeln). #384: ersätter den stale
 * mock-stämpeln. Översikt-sidan är `force-dynamic` och beräknar notiserna LIVE
 * per request, så render-tiden ÄR den ärliga "senast uppdaterad"-tidpunkten.
 * Ren helper (injicerat datum) → deterministiskt testbar. Returnerar "–" vid
 * ogiltig input i stället för att kasta.
 */
export function formatNoticesStamp(date: Date): string {
  if (Number.isNaN(date.getTime())) return "–";
  const iso = date.toISOString();
  return `${iso.slice(0, 10)} · ${iso.slice(11, 16)}`;
}

/**
 * Returnerar nyligen bekräftade intervjuer: status === InterviewScheduled
 * och `updatedAt` ligger inom 1 UTC-kalenderdag bakåt från `now` (kan i
 * praktiken vara upp till ~47h gammal pga `daysSince`-trunkering). Driver
 * Intervju-bekräftelse-notisen — fönstret är kalenderdag-bundet, inte
 * 24h rullande, för att matcha "i går"/"i dag"-copyn.
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
 * Filtrerar deadline-poster och behåller bara de som ligger >= idag (UTC-
 * kalenderdag). Förhindrar att MOCK-deadlines i `OVERSIKT_MOCK` visar
 * "denna vecka"-notisen efter att alla datum passerat (code-reviewer M3
 * 2026-05-24). När BE-port för riktiga deadlines finns: ersätt mock-arrayen,
 * filterlogiken förblir korrekt.
 */
export function filterFutureDeadlines<
  T extends { readonly date: string },
>(deadlines: ReadonlyArray<T>, now: Date = new Date()): ReadonlyArray<T> {
  return deadlines.filter((d) => daysSince(d.date, now) <= 0);
}

