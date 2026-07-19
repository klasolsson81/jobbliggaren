import { notFound } from "next/navigation";
import { findGuestJobAd } from "@/lib/guest/mock-data";
import { toJobAdDetail } from "@/lib/guest/mock-adapters";
import { JobAdDetail } from "@/components/job-ads/job-ad-detail";
import { JobAdModalShell } from "@/components/job-ads/job-ad-modal-shell";

// F-Pre Punkt 5b 2026-05-24 — intercepting route för @modal-slotten
// (gäst-tree). Speglar `(app)/@modal/(.)jobb/[id]/page.tsx` (ADR 0053).
//
// `<JobAdDetail>` återanvänds (CTO Beslut 6) — den är ren presentational
// RSC och muterande knappar (SaveJobAdToggle / HarAnsoktButton) renderas
// ENDAST när `initialSaved` + `initialApplied` är definierade (job-ad-
// detail.tsx:64-67 userActions-narrowing). Vi passar `undefined` (default)
// → knapparna döljs automatiskt utan att gäst behöver egna shell.

interface PageProps {
  params: Promise<{ id: string }>;
}

export default async function InterceptedGuestJobbModal({
  params,
}: PageProps) {
  const { id } = await params;
  const mock = findGuestJobAd(id);
  if (!mock) notFound();

  const jobAd = toJobAdDetail(mock);

  return (
    <JobAdModalShell title={jobAd.title} company={jobAd.companyName}>
      <JobAdDetail jobAd={jobAd} headless />
    </JobAdModalShell>
  );
}
