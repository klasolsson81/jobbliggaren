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

// #1572 — sidan bär appens `<NoticeToolbar>`, vars uppdatera-kontroll bygger på att en
// ny render ger en NY stämpel. Det kräver att routen är dynamisk.
//
// Mätt i byggets route-tabell: den är redan `ƒ`, men av ett skäl som inte bor här —
// ROT-layoutens `getLocale()` läser en request-header och gör HELA appen dynamisk (varje
// rad i tabellen är `ƒ`). Raden nedan är alltså redundant i dag, och står ändå: det som
// gör stämpeln sann är en egenskap hos DEN HÄR sidan, och utan deklarationen skulle en
// framtida i18n-omläggning frysa den vid byggtid utan att något här säger emot. Samma
// deklaration, av samma skäl och med samma redundans, som `(app)/oversikt/page.tsx:36`.
export const dynamic = "force-dynamic";

export default function GuestOversiktRoute() {
  return (
    <>
      <GuestDemoBanner />
      <GuestOversiktPage />
    </>
  );
}
