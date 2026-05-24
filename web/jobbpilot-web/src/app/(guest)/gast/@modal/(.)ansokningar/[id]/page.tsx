import { notFound } from "next/navigation";
import { findGuestApplication } from "@/lib/guest/mock-data";
import { ApplicationModalShell } from "@/components/applications/application-modal-shell";
import { GuestApplicationDetail } from "@/components/guest/guest-application-detail";

// F-Pre Punkt 5b 2026-05-24 — intercepting route för @modal-slotten i
// gäst-tree. Speglar `(app)/@modal/(.)ansokningar/[id]/page.tsx`
// (ADR 0053). `(.)ansokningar/[id]` matchar samma segment-nivå som
// slot-monteringspunkten `(guest)/gast` — `@modal` är en slot, inte ett
// route-segment.
//
// Soft-nav (radklick → Link /gast/ansokningar/[id]) fångas här → modal.
// Hard-nav / refresh / delad länk träffar `/gast/ansokningar/[id]/page.tsx`
// (fullsida).
//
// Mockdata-uppslag via `findGuestApplication(id)`. Ingen BE-anrop, ingen
// session-check (gäst-tree är anonym). `ApplicationModalShell` återanvänds
// från live (modal-chrome är ren — bara stänger via router.back()).

interface PageProps {
  params: Promise<{ id: string }>;
}

export default async function InterceptedGuestAnsokanModal({
  params,
}: PageProps) {
  const { id } = await params;
  const application = findGuestApplication(id);
  if (!application) notFound();

  return (
    <ApplicationModalShell
      title={application.role}
      subtitle={`${application.company} · demo`}
    >
      <GuestApplicationDetail application={application} />
    </ApplicationModalShell>
  );
}
