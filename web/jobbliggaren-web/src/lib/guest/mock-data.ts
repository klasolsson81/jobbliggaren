// F-Pre Punkt 5 — Gäst-mode mockdata (Klas-direktiv 2026-05-24 + CTO-dom
// `docs/reviews/2026-05-24-fpre-punkt5-cto.md` Beslut 5).
//
// Importerar och återexporterar `OVERSIKT_MOCK` från `lib/oversikt/mock-data.ts`
// (DRY — samma "applications", "cv" mockdata används av båda trees så att
// /gast/oversikt och /gast/ansokningar är synkade per Klas-direktiv §E).
// Gäst-specifika fält (ansokningar-pipeline-snippet, cv-grid-snippet) bor här
// så `(guest)/*` är SRP-isolerad från `(app)`-tree.

import { OVERSIKT_MOCK } from "@/lib/oversikt/mock-data";

// Re-export så gäst-tree:s konsumenter slipper dubbel-import.
export { OVERSIKT_MOCK };

// Single source of truth för gäst-mockens "nu"-referens (code-reviewer Minor 2
// + design-reviewer m5 2026-05-24). Tidigare duplicerad i
// `guest-oversikt-page.tsx` (GUEST_DEMO_TODAY) och `mock-adapters.ts`
// (REF_NOW) — drift-risk vid demo-refresh. Frozen ISO så vitest-snapshots inte
// driftar. Uppdatera vid demo-refresh på en plats.
export const GUEST_MOCK_REF_DATE_ISO = "2026-05-24T08:00:00Z";
export const GUEST_MOCK_REF_DATE = new Date(GUEST_MOCK_REF_DATE_ISO);
export const GUEST_MOCK_REF_NOW_MS = GUEST_MOCK_REF_DATE.getTime();

export type GuestApplicationStatus =
  | "Draft"
  | "Submitted"
  | "Interview"
  | "Offer"
  | "Rejected";

export interface GuestMockApplication {
  readonly id: string;
  readonly company: string;
  readonly role: string;
  readonly status: GuestApplicationStatus;
  readonly statusLabel: string;
  readonly updatedAtIso: string;
  readonly source: string;
}

export interface GuestMockResume {
  readonly id: string;
  readonly title: string;
  readonly language: "sv" | "en";
  readonly latestRole: string | null;
  readonly sectionCount: number;
  readonly topSkills: ReadonlyArray<string>;
  readonly updatedAtIso: string;
  readonly isPrimary: boolean;
}

export interface GuestMockJobAd {
  readonly id: string;
  readonly title: string;
  readonly companyName: string;
  readonly source: "Platsbanken" | "Manual";
  readonly publishedAtIso: string;
  readonly expiresAtIso: string | null;
  readonly summary: string;
  readonly description: string;
  readonly url: string;
}

export interface GuestMockData {
  readonly applications: ReadonlyArray<GuestMockApplication>;
  readonly resumes: ReadonlyArray<GuestMockResume>;
  readonly jobAds: ReadonlyArray<GuestMockJobAd>;
  // Sammanfattnings-tal som synkas med /gast/oversikt:s "Mina ansökningar"-rad
  // och /gast/ansokningar:s pipeline-count. Härleds från `applications` ovan
  // (single source of truth — uppdatera mockdata på en plats).
  readonly summary: {
    readonly applicationsTotal: number;
    readonly applicationsByStatus: Readonly<Record<GuestApplicationStatus, number>>;
    readonly resumesTotal: number;
    readonly jobAdsTotal: number;
  };
  /** Mock-totalt aktiva annonser i korpus (design-reviewer M5). */
  readonly activeJobAdsTotal: number;
}

// Konsistent demo-pipeline: 7 ansökningar fördelade över alla 5 status så
// /gast/oversikt:s "Sammanfattning" och /gast/ansokningar:s pipeline matchar.
const APPLICATIONS: ReadonlyArray<GuestMockApplication> = [
  {
    id: "ga-1",
    company: "Klarna",
    role: "Backend-utvecklare",
    status: "Interview",
    statusLabel: "Intervju",
    updatedAtIso: "2026-05-23T14:20:00Z",
    source: "Platsbanken",
  },
  {
    id: "ga-2",
    company: "Folksam IT",
    role: "Systemutvecklare .NET",
    status: "Submitted",
    statusLabel: "Inskickad",
    updatedAtIso: "2026-05-21T09:05:00Z",
    source: "Platsbanken",
  },
  {
    id: "ga-3",
    company: "Skatteverket",
    role: "Lösningsarkitekt",
    status: "Offer",
    statusLabel: "Erbjudande",
    updatedAtIso: "2026-05-24T07:15:00Z",
    source: "Manual",
  },
  {
    id: "ga-4",
    company: "Bonnier News",
    role: "Verksamhetsutvecklare",
    status: "Submitted",
    statusLabel: "Inskickad",
    updatedAtIso: "2026-05-19T16:40:00Z",
    source: "Platsbanken",
  },
  {
    id: "ga-5",
    company: "ICA Gruppen",
    role: "Fullstack-utvecklare",
    status: "Draft",
    statusLabel: "Utkast",
    updatedAtIso: "2026-05-22T11:30:00Z",
    source: "Manual",
  },
  {
    id: "ga-6",
    company: "Region Stockholm",
    role: "Backend-utvecklare junior",
    status: "Rejected",
    statusLabel: "Avslag",
    updatedAtIso: "2026-05-17T13:00:00Z",
    source: "Platsbanken",
  },
  {
    id: "ga-7",
    company: "Trafikverket",
    role: "Webbutvecklare",
    status: "Draft",
    statusLabel: "Utkast",
    updatedAtIso: "2026-05-20T08:50:00Z",
    source: "Platsbanken",
  },
];

const RESUMES: ReadonlyArray<GuestMockResume> = [
  {
    id: "gr-1",
    title: "CV: Systemutveckling",
    language: "sv",
    latestRole: "Junior systemutvecklare",
    sectionCount: 6,
    topSkills: ["C#", ".NET", "PostgreSQL", "Next.js"],
    updatedAtIso: "2026-05-24T06:30:00Z",
    isPrimary: true,
  },
  {
    id: "gr-2",
    title: "CV: Frontend",
    language: "sv",
    latestRole: "Webbutvecklare praktik",
    sectionCount: 5,
    topSkills: ["TypeScript", "React", "CSS", "Accessibility"],
    updatedAtIso: "2026-05-22T15:10:00Z",
    isPrimary: false,
  },
  {
    id: "gr-3",
    title: "Resume: English version",
    language: "en",
    latestRole: "Junior Software Developer",
    sectionCount: 6,
    topSkills: ["C#", ".NET", "REST APIs", "Git"],
    updatedAtIso: "2026-05-19T10:00:00Z",
    isPrimary: false,
  },
];

function countByStatus(
  apps: ReadonlyArray<GuestMockApplication>
): Record<GuestApplicationStatus, number> {
  const counts: Record<GuestApplicationStatus, number> = {
    Draft: 0,
    Submitted: 0,
    Interview: 0,
    Offer: 0,
    Rejected: 0,
  };
  for (const app of apps) {
    counts[app.status] += 1;
  }
  return counts;
}

// design-reviewer M5 2026-05-24: "Aktiva annonser totalt" på /gast/oversikt
// hade hårdkodat `46_000` i komponenten. Flyttat hit för single-source-of-
// truth + magic-number-avhjälpning. Mock-värde av samma storleksordning som
// dev-korpus (~46k aktiva annonser) så användaren får realistisk demo.
const GUEST_ACTIVE_JOB_ADS_TOTAL = 46_000;

// F-Pre Punkt 5b 2026-05-24 — mock-jobbannonser för /gast/jobb (CTO Beslut 2).
// Stabila ISO-dates (inte Date.now()) så vitest-snapshots inte driftar.
// Datum-strängarna är "färska" mot referens-stämpel 2026-05-24.
const GUEST_JOB_ADS: ReadonlyArray<GuestMockJobAd> = [
  {
    id: "gj-1",
    title: "Senior Backend-utvecklare .NET",
    companyName: "Klarna",
    source: "Platsbanken",
    publishedAtIso: "2026-05-23T08:14:00Z",
    expiresAtIso: "2026-06-15",
    summary:
      "Vi söker en senior backend-utvecklare som vill bygga betalningstjänster i .NET och Azure.",
    description:
      "Du blir en del av ett team som äger en av Klarnas centrala betalningsdomäner. Du designar API:er, arbetar med event-drivna arkitekturer (Kafka), och tar ansvar för kvalitet, observabilitet och säkerhet. Vi värdesätter erfarenhet av Clean Architecture, CQRS och testdriven utveckling.\n\nDu får jobba med moderna tekniker, distribuerad arkitektur och ett team som värnar om hantverk och pedagogik. Vi har kontor i Stockholm men erbjuder hybrid- och distansarbete.\n\nKvalifikationer: 5+ års erfarenhet av .NET, gärna med microservices, samt vana av att äga produktionssystem.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-1",
  },
  {
    id: "gj-2",
    title: "Systemutvecklare .NET",
    companyName: "Folksam IT",
    source: "Platsbanken",
    publishedAtIso: "2026-05-22T11:30:00Z",
    expiresAtIso: "2026-06-10",
    summary:
      "Folksam IT söker en systemutvecklare som vill arbeta med försäkringsdomänen.",
    description:
      "Folksam IT bygger system som hanterar miljontals försäkringsärenden årligen. Vi söker dig som tycker om att förstå domänen och skriva ren, testbar kod. Du blir del av ett tvärfunktionellt team med UX, produkt och drift.\n\nVi arbetar med .NET 8, EF Core, Postgres och Azure. Kunskap om DDD, CQRS och event sourcing är meriterande men inte krav.\n\nKollektivavtal, tjänstepension och 30 dagars semester.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-2",
  },
  {
    id: "gj-3",
    title: "Frontend-utvecklare React/Next.js",
    companyName: "Bonnier News",
    source: "Platsbanken",
    publishedAtIso: "2026-05-24T07:00:00Z",
    expiresAtIso: "2026-06-21",
    summary:
      "Bonnier News bygger nästa generations nyhetsupplevelser i React och Next.js.",
    description:
      "Du blir en del av redaktionella produktteam som arbetar nära journalister och designers. Vi använder Next.js App Router, RSC och TypeScript. Du värnar om tillgänglighet, prestanda och underhållbarhet.\n\nVi söker dig med god förståelse för React Server Components, modernt CSS och systemdesign. Erfarenhet av tillgänglighetsarbete (WCAG) är starkt meriterande.\n\nKontor i centrala Stockholm. Hybrid 2-3 dagar i veckan.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-3",
  },
  {
    id: "gj-4",
    title: "Lösningsarkitekt, offentlig sektor",
    companyName: "Skatteverket",
    source: "Platsbanken",
    publishedAtIso: "2026-05-21T13:45:00Z",
    expiresAtIso: "2026-06-12",
    summary:
      "Skatteverket söker en lösningsarkitekt för förvaltning av centrala system.",
    description:
      "Du arbetar nära beställare och utvecklingsteam för att utforma långsiktigt hållbara lösningar. Du tar fram arkitekturbeslut (ADR), genomför reviews och bidrar till våra principer för säkerhet och datakvalitet.\n\nKrav: bred erfarenhet av systemdesign, dokumentation och kommunikation. Erfarenhet av offentlig sektor och regelefterlevnad är meriterande.\n\nFasta arbetstider, statlig pension, möjlighet till distansarbete upp till två dagar i veckan.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-4",
  },
  {
    id: "gj-5",
    title: "Fullstack-utvecklare",
    companyName: "ICA Gruppen",
    source: "Platsbanken",
    publishedAtIso: "2026-05-20T09:20:00Z",
    expiresAtIso: "2026-06-08",
    summary:
      "ICA digital söker en fullstack-utvecklare till handel- och kunddata-teamet.",
    description:
      "Du arbetar med både frontend (TypeScript, React) och backend (.NET, Postgres). Teamet ansvarar för kundklubben och digitala erbjudanden, system som möter miljoner kunder dagligen.\n\nVi värdesätter pragmatism, testdisciplin och förståelse för hela leveranskedjan från idé till produktion.\n\nKontor i Solna. Hybrid.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-5",
  },
  {
    id: "gj-6",
    title: "Junior Backend-utvecklare",
    companyName: "Region Stockholm",
    source: "Platsbanken",
    publishedAtIso: "2026-05-18T14:10:00Z",
    expiresAtIso: "2026-06-05",
    summary:
      "Region Stockholm utvecklar vårdens digitala stödsystem och söker en junior utvecklare.",
    description:
      "Du blir en del av ett team som bygger e-tjänster för invånare och vårdpersonal. Du får mentor från första dagen och tydliga utvecklingsmål.\n\nKrav: avslutad utbildning inom systemvetenskap eller motsvarande. Kunskap om .NET eller Java. Vi värdesätter förmåga att läsa och förstå befintlig kod lika högt som att skriva ny.\n\nKollektivavtal och pension via region-avtal.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-6",
  },
  {
    id: "gj-7",
    title: "Webbutvecklare, tillgänglighet",
    companyName: "Trafikverket",
    source: "Platsbanken",
    publishedAtIso: "2026-05-19T10:00:00Z",
    expiresAtIso: "2026-06-09",
    summary:
      "Trafikverket söker en webbutvecklare med fokus på tillgänglighet och WCAG-efterlevnad.",
    description:
      "Du arbetar i tvärfunktionella team och stöttar både utvecklare och designers i tillgänglighetsfrågor. Du genomför audits, ger feedback på komponentbibliotek och bygger gemensamma riktlinjer.\n\nKrav: dokumenterad erfarenhet av WCAG 2.1 AA, semantisk HTML och ARIA. Kunskap om svensk DOS-lagen är meriterande.\n\nStandard statliga villkor.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-7",
  },
  {
    id: "gj-8",
    title: "DevOps-ingenjör",
    companyName: "Spotify",
    source: "Platsbanken",
    publishedAtIso: "2026-05-22T16:55:00Z",
    expiresAtIso: "2026-06-19",
    summary:
      "Spotify söker en DevOps-ingenjör som vill arbeta med plattformsteam.",
    description:
      "Du designar och vidareutvecklar interna utvecklingsplattformar som tusentals ingenjörer använder dagligen. Fokus på utvecklarupplevelse, observabilitet och CI/CD.\n\nVi arbetar med Kubernetes, Backstage, GCP och egna interna verktyg. Erfarenhet av distributed systems på skala krävs.\n\nKontor i Stockholm. Flexibelt hybridläge.",
    url: "https://arbetsformedlingen.se/platsbanken/annonser/exempel-gj-8",
  },
];

export const GUEST_MOCK: GuestMockData = {
  applications: APPLICATIONS,
  resumes: RESUMES,
  jobAds: GUEST_JOB_ADS,
  summary: {
    applicationsTotal: APPLICATIONS.length,
    applicationsByStatus: countByStatus(APPLICATIONS),
    resumesTotal: RESUMES.length,
    jobAdsTotal: GUEST_JOB_ADS.length,
  },
  activeJobAdsTotal: GUEST_ACTIVE_JOB_ADS_TOTAL,
} as const;

/** Slå upp gäst-mock-annons via id. Returnerar `null` om id okänt. */
export function findGuestJobAd(id: string): GuestMockJobAd | null {
  return GUEST_JOB_ADS.find((j) => j.id === id) ?? null;
}

/** Slå upp gäst-mock-ansökan via id. Returnerar `null` om id okänt. */
export function findGuestApplication(id: string): GuestMockApplication | null {
  return APPLICATIONS.find((a) => a.id === id) ?? null;
}

// Pipeline-grupperingen för /gast/ansokningar — samma data, omstrukturerad
// efter status. Härleds (inte hårdkodad) så summan = applications.length och
// alltid synk med /gast/oversikt:s tal.
export interface GuestPipelineGroup {
  readonly status: GuestApplicationStatus;
  readonly statusLabel: string;
  readonly count: number;
  readonly applications: ReadonlyArray<GuestMockApplication>;
}

const STATUS_ORDER: ReadonlyArray<{ status: GuestApplicationStatus; label: string }> = [
  { status: "Draft", label: "Utkast" },
  { status: "Submitted", label: "Inskickad" },
  { status: "Interview", label: "Intervju" },
  { status: "Offer", label: "Erbjudande" },
  { status: "Rejected", label: "Avslag" },
];

export function buildGuestPipeline(): ReadonlyArray<GuestPipelineGroup> {
  return STATUS_ORDER.map(({ status, label }) => {
    const apps = APPLICATIONS.filter((a) => a.status === status);
    return {
      status,
      statusLabel: label,
      count: apps.length,
      applications: apps,
    };
  });
}

// #1572 — bevakade företag för Företagsbevaknings stående tillstånd på
// /gast/oversikt. Egen konstant och inte en del av `GUEST_MOCK`: den konsumeras
// bara via `mock-adapters.ts` (som `CompanyWatch[]`), medan `GuestMockData` är
// gäst-tree:ts egna former.
//
// `organizationNumber` bär tio siffror som FALLER PÅ LUHN och alltså inte kan
// vara ett verkligt org.nr (repot är publikt). Formen är ändå den producerbara:
// en juridisk person, `isProtectedIdentity: false`. Ett `null` hade i stället
// påstått skyddad identitet, som dto:n reserverar för personnummer-formade
// org.nr. Värdena renderas aldrig — `CompanySummary` bevisar det i markup i sitt
// eget test — så de finns bara för att formen ska vara hel.
export interface GuestMockCompanyWatch {
  readonly id: string;
  readonly organizationNumber: string;
  readonly companyName: string;
  readonly followedAt: string;
  readonly activeAdCount: number;
  readonly matchingAdCount: number;
  readonly hasFilter: boolean;
}

const COMPANY_WATCHES: ReadonlyArray<GuestMockCompanyWatch> = [
  {
    id: "gw-1",
    organizationNumber: "5590000001",
    companyName: "Klarna",
    followedAt: "2026-05-18T09:00:00Z",
    activeAdCount: 14,
    matchingAdCount: 3,
    hasFilter: false,
  },
  {
    id: "gw-2",
    organizationNumber: "5560000002",
    companyName: "Skatteverket",
    followedAt: "2026-05-20T13:30:00Z",
    activeAdCount: 9,
    matchingAdCount: 2,
    // Inget filter i demot. Sammanfattningens filter-brasklapp löses på appytan av
    // "Visa bevakade företag"; gästytan renderar ingen länk, så raden hade pekat på
    // en inställning besökaren varken kan se eller nå (`design-reviewer` Minor 3).
    hasFilter: false,
  },
  {
    id: "gw-3",
    organizationNumber: "5561000003",
    companyName: "Region Stockholm",
    followedAt: "2026-05-22T07:45:00Z",
    activeAdCount: 0,
    matchingAdCount: 0,
    hasFilter: false,
  },
];

/** Bevakade företag i demot. Se `toCompanyWatches` i `mock-adapters.ts`. */
export const GUEST_COMPANY_WATCHES = COMPANY_WATCHES;

/**
 * Nya annonser från bevakade företag sedan "senaste besöket" — driver
 * Företagsbevaknings-notisen. Ett tal och inte en härledning: appens motsvarighet
 * är ett delta mot en watermark som demot inte har, så ett summerat
 * `activeAdCount` hade påstått en annan storhet än den notisen namnger.
 */
export const GUEST_NEW_FOLLOWED_COMPANY_AD_COUNT = 4;
