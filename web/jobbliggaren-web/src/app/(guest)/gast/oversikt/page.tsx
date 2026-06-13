import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { GuestOversiktPage } from "@/components/guest/guest-oversikt-page";

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
