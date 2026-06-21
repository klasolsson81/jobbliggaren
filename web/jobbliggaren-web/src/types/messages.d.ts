// Typed messages for next-intl (v4): augment `AppConfig` so `useTranslations`
// and `getTranslations` namespace/key arguments are checked against the Swedish
// catalog (the source of truth). See https://next-intl.dev/docs/workflows/typescript.
import type svMessages from "../../messages/sv";
import type { Locale } from "@/i18n/routing";

declare module "next-intl" {
  interface AppConfig {
    Locale: Locale;
    Messages: typeof svMessages;
  }
}
