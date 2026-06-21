import type { Metadata } from "next";
import { Hanken_Grotesk, JetBrains_Mono } from "next/font/google";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { ThemeProvider, ThemeScript } from "@/components/theme-provider";
import "./globals.css";

const hankenGrotesk = Hanken_Grotesk({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-sans",
  display: "swap",
});

const jetBrainsMono = JetBrains_Mono({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
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
  const messages = await getMessages();

  return (
    <html
      lang={locale}
      data-density="standard"
      suppressHydrationWarning
      className={`${hankenGrotesk.variable} ${jetBrainsMono.variable} h-full font-sans`}
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
