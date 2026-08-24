import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { GuestOversiktPage } from "@/components/guest/guest-oversikt-page";
import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("guest");
  return { title: t("oversikt.meta.title") };
}

// F-Pre Punkt 5 — Gäst-översikt (CTO-dom 2026-05-24 Beslut 1).
//
// Server Component. Ingen `getServerSession()`-grind (gäst-tree). Renderar
// `<GuestOversiktPage>` med mock-data från `lib/guest/mock-data.ts`.

export default function GuestOversiktRoute() {
  return (
    <>
      <GuestDemoBanner />
      <GuestOversiktPage />
    </>
  );
}
