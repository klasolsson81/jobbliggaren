import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { JobAdDetail } from "@/components/job-ads/job-ad-detail";
import { findGuestJobAd } from "@/lib/guest/mock-data";
import { toJobAdDto } from "@/lib/guest/mock-adapters";

// F-Pre Punkt 5b 2026-05-24 — fullsida för hard-nav till `/gast/jobb/[id]`.
// Soft-nav fångas av intercepting route → modal.

interface PageProps {
  params: Promise<{ id: string }>;
}

export default async function GuestJobbFullPage({ params }: PageProps) {
  const { id } = await params;
  const mock = findGuestJobAd(id);
  if (!mock) notFound();

  const jobAd = toJobAdDto(mock);

  return (
    <>
      <GuestDemoBanner />
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">Demoannons</div>
            <h1 className="jp-pagehero__title">{jobAd.title}</h1>
            <p className="jp-pagehero__lede">{jobAd.companyName}</p>
          </div>
          <div className="jp-pagehero__aside">
            <Link
              href="/gast/jobb"
              className="jp-btn jp-btn--secondary jp-btn--sm"
            >
              <ArrowLeft size={14} aria-hidden="true" /> Till listan
            </Link>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <JobAdDetail jobAd={jobAd} />
      </div>
    </>
  );
}
