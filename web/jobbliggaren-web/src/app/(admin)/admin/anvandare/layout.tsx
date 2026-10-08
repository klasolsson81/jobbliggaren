import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import { ReloadedAfterUpdateNotice } from "@/components/site/reloaded-after-update-notice";
import { pickClientMessages } from "@/i18n/client-messages";

export default async function AdminAccountsLayout({ children }: { children: React.ReactNode }) {
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["admin", "common", "fallback", "pages", "settings"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <ReloadedAfterUpdateNotice placement="inline" />
      {children}
    </NextIntlClientProvider>
  );
}
