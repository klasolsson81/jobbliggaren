import { useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import { InformationLink } from "@/components/information/InformationLink";
import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";
import { adSourceLink } from "@/lib/job-ads/ad-source-link";
import { formatAdDescription } from "./format-ad-description";
import { JobAdDetailMeta } from "./job-ad-detail-meta";
import { StripBounceMarker } from "./strip-bounce-marker";
import contactLinkStyles from "./recruiter-contact-link.module.css";

/**
 * The anonymous job page (ADR 0165): one listed ad, read by anyone, as the server sends it.
 *
 * A composition of its own, not `JobAdDetail`. That component carries the signed-in person's actions (save,
 * follow, applied, match, contacts) behind optional props, and a public page that reached it would be one
 * forgotten prop away from showing them. This one takes `PublicJobAdDto` and can render nothing else.
 *
 * The whole description is on the page and in the first response: no excerpt, no toggle, no script needed to read
 * it (the epic's "full ad text without JS"). It is rendered by `formatAdDescription`, which hands the text to React
 * as children, so it is escaped and never HTML (ADR 0160 D9.6).
 *
 * `status="Active"` is a fact, not a default: a listed ad is Active by the publication predicate (ADR 0160 D2).
 */
export function PublicJobAd({ jobAd }: { jobAd: PublicJobAdDto }) {
  const tPublic = useTranslations("publicjob");
  const tUi = useTranslations("jobads.ui");
  const link = adSourceLink(jobAd.url);

  return (
    <main id="main" tabIndex={-1} className="jp-container jp-page focus:outline-none">
      <StripBounceMarker />
      <article className="jp-modal jp-modal--page jp-modal--public">
        <header className="jp-modal__head">
          <div className="flex-1">
            <h1 className="jp-modal__title">{jobAd.title}</h1>
            <p className="jp-modal__company">{jobAd.companyName}</p>
            <JobAdDetailMeta
              jobAd={{ status: "Active", publishedAt: jobAd.publishedAt, expiresAt: jobAd.expiresAt }}
            />
          </div>
        </header>

        <div className="jp-modal__body">
          <section aria-labelledby="jp-ad-description-title">
            <div id="jp-ad-description-title" className="jp-eyebrow mb-2">
              {tUi("detail.description")}
            </div>
            <div className="jp-modal__description">{formatAdDescription(jobAd.description, 2)}</div>
          </section>

          <p className="jp-recruiter-notice">
            <InformationLink
              id={`information-job-${jobAd.id}`}
              href="/kontaktperson-i-annons"
              className={contactLinkStyles.link}
            >
              {tUi("detail.recruiterNoticeLink")}
            </InformationLink>
          </p>
        </div>

        {link && (
          <div className="jp-modal__foot jp-modal__foot--split">
            <div className="jp-modal__footgroup jp-modal__footgroup--end">
              <a href={link.href} target="_blank" rel="noopener noreferrer" className="jp-btn jp-btn--primary">
                <ExternalLink size={14} aria-hidden="true" /> {tPublic(link.label)}
              </a>
            </div>
          </div>
        )}
      </article>
    </main>
  );
}
