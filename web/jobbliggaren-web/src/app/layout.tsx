import type { Metadata } from "next";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { documentFontClassName } from "./fonts";
import { ThemeProvider, ThemeScript } from "@/components/theme-provider";
import "./globals.css";
import { InformationReturnProvider } from "@/components/information/InformationReturnProvider";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("metadata");
  const locale = await getLocale();

  return {
    metadataBase: new URL(
      process.env.NEXT_PUBLIC_SITE_URL ?? "https://dev.jobbliggaren.se"
    ),
    title: {
      default: t("titleDefault"),
      template: t("titleTemplate"),
    },
    description: t("description"),
    applicationName: t("applicationName"),
    // icons/openGraph/twitter/manifest plockas upp automatiskt av Next.js 16
    // file-conventions (app/icon.svg, app/apple-icon.tsx, app/opengraph-image.tsx,
    // app/twitter-image.tsx, app/manifest.ts) — explicit metadata-fält behövs inte.
    openGraph: {
      type: "website",
      locale: locale === "sv" ? "sv_SE" : "en_US",
      siteName: t("applicationName"),
    },
    twitter: {
      card: "summary_large_image",
    },
  };
}

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const locale = await getLocale();
  // #737 — the root boundary's own client subtree (ThemeScript/ThemeProvider)
  // reads NO messages, so this payload is EMPTY by measurement, not by choice:
  // the fitness function computes it from the import graph and fails if a
  // namespace is added here without a client consumer. Root is the one boundary
  // where that matters most — it wraps every route, so anything it carries is
  // paid by every document ON TOP of the nested boundary's own set.
  //
  // The provider itself stays: it supplies locale + timeZone to client
  // formatters (useFormatter) even with no messages. Route boundaries below
  // ((app)/(auth)/(guest)/(marketing)/(marketing-inner)/(admin)) each render
  // their own provider with their own set — React context replaces, not merges.
  const messages = pickClientMessages(await getMessages(), []);

  return (
    <html
      lang={locale}
      suppressHydrationWarning
      className={documentFontClassName}
    >
      <body className="min-h-full bg-surface-primary text-text-primary antialiased">
        <ThemeScript />
        <NextIntlClientProvider locale={locale} messages={messages}>
          <InformationReturnProvider><ThemeProvider>{children}</ThemeProvider></InformationReturnProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
