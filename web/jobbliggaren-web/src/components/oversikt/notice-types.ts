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
