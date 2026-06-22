// Single source of truth for the supported locales and the locale cookie.
// The app uses next-intl WITHOUT i18n routing (no `[locale]` URL segment): the
// active locale is resolved per request from the `NEXT_LOCALE` cookie in
// `request.ts`. Swedish is the default and canonical locale; English is a
// secondary convenience locale. See ADR 0078.
export const locales = ["sv", "en"] as const;

export type Locale = (typeof locales)[number];

export const defaultLocale: Locale = "sv";

// next-intl's conventional cookie name. We read/write it ourselves rather than
// relying on next-intl middleware (which exists only for i18n routing).
export const LOCALE_COOKIE = "NEXT_LOCALE";

export function isLocale(value: string | null | undefined): value is Locale {
  return value === "sv" || value === "en";
}
