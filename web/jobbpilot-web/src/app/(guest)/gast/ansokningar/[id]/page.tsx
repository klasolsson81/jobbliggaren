import Link from "next/link";
import { notFound } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { GuestDemoBanner } from "@/components/guest/guest-demo-banner";
import { GuestApplicationDetail } from "@/components/guest/guest-application-detail";
import { findGuestApplication } from "@/lib/guest/mock-data";

// F-Pre Punkt 5b 2026-05-24 — fullsida för hard-nav till
// `/gast/ansokningar/[id]` (refresh, delad länk, "öppna i ny flik").
// Soft-nav fångas av intercepting route → modal. Båda kontexter renderar
// samma `<GuestApplicationDetail>` (DRY).

interface PageProps {
  params: Promise<{ id: string }>;
}

export default async function GuestAnsokanFullPage({ params }: PageProps) {
  const { id } = await params;
  const application = findGuestApplication(id);
  if (!application) notFound();

  return (
    <>
      <GuestDemoBanner />
      <section className="jp-pagehero">
        <div className="jp-pagehero__inner">
          <div className="jp-pagehero__main">
            <div className="jp-pagehero__kicker">Demoansökan</div>
            <h1 className="jp-pagehero__title">{application.role}</h1>
            <p className="jp-pagehero__lede">
              {application.company} · {application.source}
            </p>
          </div>
          <div className="jp-pagehero__aside">
            <Link
              href="/gast/ansokningar"
              className="jp-btn jp-btn--secondary jp-btn--sm"
            >
              <ArrowLeft size={14} aria-hidden="true" /> Till listan
            </Link>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        <GuestApplicationDetail application={application} />
      </div>
    </>
  );
}
