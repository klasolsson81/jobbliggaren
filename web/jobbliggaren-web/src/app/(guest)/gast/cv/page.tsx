import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { GuestCvPage } from "@/components/guest/guest-cv-page";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("guest");
  return { title: t("cv.meta.title") };
}

// F-Pre Punkt 5 — Gäst-CV (CTO-dom 2026-05-24 Beslut 1).

export default function GuestCvRoute() {
  return (
    <>
      <GuestDemoBanner />
      <GuestCvPage />
    </>
  );
}
