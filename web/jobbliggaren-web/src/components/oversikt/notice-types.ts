import type { ReactNode } from "react";

/**
 * RSC-safe home for the notice-type SSOT (#726 regression fix). NO "use client" directive:
 * NOTICE_TYPES is a runtime value read by the Server Component oversikt-page.tsx. When it lived
 * in the "use client" notice-section.tsx, importing it into a Server Component turned it into a
 * client reference — undefined on the server — so `NOTICE_TYPES[source].map(...)` threw during
 * server render and /oversikt crashed for signed-in users. Keeping the value plus its derived
 * types in this plain module lets both the Server Component and the client notice-section.tsx
 * import the real object.
 */
export type NoticeSource = "applications" | "jobads" | "companies";

/**
 * SSOT för notis-typerna per källa (code-reviewer Minor 1, #726): notis-
 * konstruktionen (`SectionNoticeData.type`), kugghjuls-popoverns rader och
 * pref-nycklarna `"<source>:<type>"` läser ALLA denna tabell — en felstavad
 * typ-slug blir ett kompileringsfel i stället för en tyst trasig filtrering.
 * Typer utan notis ännu ("statuschanges", "companyevents") är förberedda
 * popover-val per handoffen.
 */
export const NOTICE_TYPES = {
  applications: ["followup", "interviews", "offers", "statuschanges"],
  jobads: ["deadlines", "matches", "latestsearch"],
  companies: ["followedads", "companyevents"],
} as const satisfies Record<NoticeSource, ReadonlyArray<string>>;

export type NoticeType<S extends NoticeSource = NoticeSource> =
  (typeof NOTICE_TYPES)[S][number];

export type NoticeKind = "info" | "warning" | "brand" | "success";

/**
 * One notice as every list on `/oversikt` renders it. Lives here, not in a component module: the
 * RSC orchestrator, the shared list hook and the two list cards all type against it, and a
 * contract must not depend on the view that happens to render it (dotnet-architect, 2026-09-13).
 */
export interface NoticeData {
  readonly id: string;
  readonly kind: NoticeKind;
  readonly label: string;
  readonly text: ReactNode;
  readonly cta: string;
  readonly href: string;
  readonly time: string;
  /**
   * F4-12 PR-B (ADR 0076): en notis kan vara icke-avfärdbar (default `true`).
   * `false` på den persistenta setup-nudgen — den ska inte gå att markera som
   * läst, den löses upp först när användaren angett ett yrke. Då renderas
   * ingen dismiss-knapp (X).
   */
  readonly dismissible?: boolean;
}

/**
 * En notis i en källsektion. Utökar {@link NoticeData} med `source` + `type` för
 * inställnings-filtrering och "markera alla"-omfattning (#726). Mappad union:
 * `type` måste tillhöra just sin `source` (compile-time-länken till
 * {@link NOTICE_TYPES}).
 */
export type SectionNoticeData = {
  [S in NoticeSource]: NoticeData & {
    readonly source: S;
    readonly type: NoticeType<S>;
  };
}[NoticeSource];
