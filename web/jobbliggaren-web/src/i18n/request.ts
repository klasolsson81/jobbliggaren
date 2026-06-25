import { cookies } from "next/headers";
import { getRequestConfig } from "next-intl/server";
import enMessages from "../../messages/en";
import svMessages from "../../messages/sv";
import { defaultLocale, isLocale, LOCALE_COOKIE, type Locale } from "./routing";

const MESSAGES = {
  sv: svMessages,
  en: enMessages,
} as const;

// next-intl request configuration. Because this app runs WITHOUT i18n routing,
// `requestLocale` (the `[locale]` segment) is always undefined here — the locale
// comes from the `NEXT_LOCALE` cookie instead. An explicit `locale` is still
// honoured so awaitable APIs like `getTranslations({ locale: "en" })` work.
export default getRequestConfig(async ({ locale }) => {
  let resolved: Locale = defaultLocale;

  if (isLocale(locale)) {
    resolved = locale;
  } else {
    const cookieStore = await cookies();
    const cookieValue = cookieStore.get(LOCALE_COOKIE)?.value;
    if (isLocale(cookieValue)) {
      resolved = cookieValue;
    }
  }

  return {
    locale: resolved,
    messages: MESSAGES[resolved],
    // A Swedish civic utility has one deterministic civic timezone. Pinning it
    // here makes useFormatter().dateTime() output stable across SSR and client
    // (no hydration drift) and silences next-intl's ENVIRONMENT_FALLBACK
    // warning. The admin audit table already hardcodes the same zone.
    timeZone: "Europe/Stockholm",
  };
});
