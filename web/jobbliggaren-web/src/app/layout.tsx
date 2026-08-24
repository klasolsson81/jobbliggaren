import type { Metadata } from "next";
import { Source_Sans_3, JetBrains_Mono } from "next/font/google";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { ThemeProvider, ThemeScript } from "@/components/theme-provider";
import "./globals.css";

// Weight ranges (LP-1 #254; font swap #549 WS4 — Hanken Grotesk → Source Sans 3
// per ADR 0091: higher x/cap 0.736, USWDS/CSN civic pedigree). Source Sans 3
// ships 200–900; the app loads only its actual consumers 400–800: 800 = the
// landing hero verb stack (.jp-land-hero__stack-verb, förslag 3a) +
// .jp-pagehero__title, 700 = brand wordmark + stat numbers. Unused weights are
// deliberately not loaded — dead weight + an extra font-fetch against the CWV
// budget (CLAUDE.md §5 / §2.5 / ADR 0045). Mono carries 400–700. NOTE (#1054):
// mono 700's written justification was ".jp-land-top__stat__num (mono, live)" —
// false twice over: that rule had no consumer (removed in #1054) and it set
// --jp-font-sans, not mono. Whether mono 700 still has a consumer is an open
// perf-lane question; changing the weight list is a rendering/perf change-reason
// and is deliberately NOT made here. Mono has NO 800 consumer, so it is not loaded.
const sourceSans3 = Source_Sans_3({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700", "800"],
  variable: "--font-sans",
  display: "swap",
});

const jetBrainsMono = JetBrains_Mono({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-mono",
  display: "swap",
});

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
      className={`${sourceSans3.variable} ${jetBrainsMono.variable} h-full font-sans`}
    >
      <body className="min-h-full bg-surface-primary text-text-primary antialiased">
        <ThemeScript />
        <NextIntlClientProvider locale={locale} messages={messages}>
          <ThemeProvider>{children}</ThemeProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
