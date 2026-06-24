// MOCK: F6 P5 Punkt 4 — mockdata för fält som saknar BE-yta.
// Per CTO-dom 2026-05-24 (agentId ac1dbfa14aa599e65) + HANDOVER §0 godkänt
// av Klas 2026-05-23. Var och en ska bytas till riktig data när motsvarande
// BE-port finns. Single source — alla konsumenter importerar härifrån så
// utbyte sker på en plats.

export interface OversiktTodayEvent {
  readonly id: string;
  readonly time: string;
  readonly title: string;
  readonly where: string | null;
  readonly source: "jobbliggaren" | "google";
}

export interface OversiktSavedJobDeadline {
  readonly date: string; // ISO yyyy-MM-dd
  readonly label: string; // svensk kortform "25 maj"
}

export interface OversiktSavedSearchHit {
  readonly name: string;
  readonly newHits: number;
}

export interface OversiktNoticeCopy {
  readonly company: string;
  readonly role?: string;
  readonly deadlineCopy?: string;
  readonly dateCopy?: string;
}

export interface OversiktMock {
  /** BE-port saknas: Google Calendar-integration framtid (HANDOVER §3.2). */
  readonly todaysEvents: ReadonlyArray<OversiktTodayEvent>;
  readonly googleSynced: boolean;
  /**
   * MOCK kvar (STEG 6 follow-up, Klas: håll STEG 6 smalt): "Nya matchningar i
   * dag"-raden i Sammanfattningen är fortfarande mock. Byts mot en live-port i
   * en separat PR. (Notis-SIFFRAN i auth-feeden är nu LIVE via
   * `getMatchCount()` — ADR 0079 STEG 6; den läser INTE detta fält.)
   */
  readonly matchCountToday: number;
  /**
   * GUEST-ONLY (efter ADR 0079 STEG 6): den autentiserade `/oversikt`-notisen
   * använder INTE längre dessa — den läser live `getMatchCount()`. Kvar enbart
   * för den publika gäst-förhandsvisningen (`guest-oversikt-page.tsx`, egna
   * `guest.json`-nycklar), som är en avsiktlig demo-yta utanför STEG 6:s scope.
   */
  readonly matchCountThisWeek: number;
  readonly matchSegmentLabel: string;
  /** BE-port saknas: Deadline-fält på SavedJobAd (HANDOVER §3.3). */
  readonly savedJobsDeadlines: ReadonlyArray<OversiktSavedJobDeadline>;
  /** BE-port saknas: letters-tabell (HANDOVER §3.6). */
  readonly personalLettersCount: number;
  /** BE-port saknas: notification-stämpel (HANDOVER §3.3). */
  readonly noticesLastUpdated: string;
  /** BE-port saknas: SavedSearches "ny-träff-count-since-last-run". */
  readonly savedSearchHitsLast: OversiktSavedSearchHit;
  /** Static notice-snippets (mock — backend-driven content saknar port). */
  readonly bonnierOffer: OversiktNoticeCopy;
  readonly folksamInterview: OversiktNoticeCopy;
}

export const OVERSIKT_MOCK: OversiktMock = {
  todaysEvents: [
    {
      id: "ev-1",
      time: "10:30",
      title: "Telefonscreening: Klarna",
      where: "Rebecca Lind, rekryterare",
      source: "jobbliggaren",
    },
    {
      id: "ev-2",
      time: "14:00",
      title: "Förbered intervju med Folksam IT",
      where: "30 min",
      source: "jobbliggaren",
    },
  ],
  googleSynced: false,
  matchCountToday: 28,
  matchCountThisWeek: 143,
  matchSegmentLabel: "Mjukvaru- och systemutvecklare",
  savedJobsDeadlines: [
    { date: "2026-05-25", label: "25 maj" },
    { date: "2026-05-27", label: "27 maj" },
  ],
  personalLettersCount: 4,
  noticesLastUpdated: "2026-05-23 · 08:42",
  savedSearchHitsLast: { name: "Remote / Distansjobb", newHits: 4 },
  bonnierOffer: {
    company: "Bonnier News",
    role: "verksamhetsutvecklare",
    deadlineCopy: "27 maj",
  },
  folksamInterview: {
    company: "Folksam IT",
    dateCopy: "tisdag 26 maj 14:00",
  },
} as const;
