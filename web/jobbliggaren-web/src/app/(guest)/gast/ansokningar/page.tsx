import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { GuestAnsokningarPage } from "@/components/guest/guest-ansokningar-page";

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
