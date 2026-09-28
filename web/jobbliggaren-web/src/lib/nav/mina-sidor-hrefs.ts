/**
 * The URL of each /mina-sidor section (#1891). A plain module rather than an export of a
 * `"use client"` file, for the reason `match-settings-href.ts` gives: a Server Component that imports
 * from a client module gets a client reference, not the string.
 *
 * Bare `/mina-sidor` is the Matchning section, and it stays a page: every link sent before the
 * sections existed points there (the notification mails' settings link included).
 */
export const MINA_SIDOR_HREF = {
  matchning: "/mina-sidor",
  notiser: "/mina-sidor/notiser",
  konto: "/mina-sidor/konto",
  sekretess: "/mina-sidor/sekretess",
} as const;
