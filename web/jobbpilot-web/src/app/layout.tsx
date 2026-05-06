import type { Metadata } from "next";
import { Hanken_Grotesk } from "next/font/google";
import "./globals.css";

const hankenGrotesk = Hanken_Grotesk({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-sans",
  display: "swap",
});

export const metadata: Metadata = {
  title: "JobbPilot",
  description: "Din jobbansökningshanterare",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="sv" className={`${hankenGrotesk.variable} h-full font-sans`}>
      <body className="min-h-full bg-surface-primary text-text-primary antialiased">
        {children}
      </body>
    </html>
  );
}
