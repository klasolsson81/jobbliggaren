import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { GuestJobbPage } from "@/components/guest/guest-jobb-page";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("guest");
  return { title: t("jobb.meta.title") };
}

// F-Pre Punkt 5b 2026-05-24 — gäst-jobb mockdata-sida (CTO Beslut 2 + 4).
// Ersätter föregående sessions konverterings-CTA. DEMO-banner visas
// eftersom datan är mock (Beslut 4 — konsekvent tillämpning av
// "DEMO = ej din riktiga data"-regeln; föregående exklusion gällde LIVE-data).

export default function GuestJobbRoute() {
  return (
    <>
      <GuestDemoBanner />
      <GuestJobbPage />
    </>
  );
}
