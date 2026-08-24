import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { GuestAnsokningarPage } from "@/components/guest/guest-ansokningar-page";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("guest");
  return { title: t("ansokningar.meta.title") };
}

// F-Pre Punkt 5 — Gäst-ansökningar (CTO-dom 2026-05-24 Beslut 1).
// Mockdata-pipeline härledd från samma `GUEST_MOCK.applications` som
// `/gast/oversikt` så summorna är synkade per Klas-direktiv §E.

export default function GuestAnsokningarRoute() {
  return (
    <>
      <GuestDemoBanner />
      <GuestAnsokningarPage />
    </>
  );
}
