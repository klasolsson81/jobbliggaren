import type { Metadata } from "next";
import { Hanken_Grotesk, JetBrains_Mono } from "next/font/google";
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

export const metadata: Metadata = {
  metadataBase: new URL(
    process.env.NEXT_PUBLIC_SITE_URL ?? "https://dev.jobbpilot.se"
  ),
  title: {
    default: "JobbPilot",
    template: "%s | JobbPilot",
  },
  description: "Den svenska jobbansökningshanteraren",
  applicationName: "JobbPilot",
  // icons/openGraph/twitter/manifest plockas upp automatiskt av Next.js 16
  // file-conventions (app/icon.svg, app/apple-icon.tsx, app/opengraph-image.tsx,
  // app/twitter-image.tsx, app/manifest.ts) — explicit metadata-fält behövs inte.
  openGraph: {
    type: "website",
    locale: "sv_SE",
    siteName: "JobbPilot",
  },
  twitter: {
    card: "summary_large_image",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="sv"
      data-density="standard"
      suppressHydrationWarning
      className={`${hankenGrotesk.variable} ${jetBrainsMono.variable} h-full font-sans`}
    >
      <body className="min-h-full bg-surface-primary text-text-primary antialiased">
        <ThemeScript />
        <ThemeProvider>{children}</ThemeProvider>
      </body>
    </html>
  );
}
