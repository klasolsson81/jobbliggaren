import { useTranslations } from "next-intl";
import { ExternalLink, FileText, MapPin } from "lucide-react";
import { InformationLink } from "@/components/information/InformationLink";
import { JsonLdScript } from "@/components/seo/json-ld-script";
import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";
import { type CodedTaxonomyId, codedTaxonomyName, isCodedTaxonomyId } from "@/lib/i18n/coded-taxonomy";
import { adSourceLink } from "@/lib/job-ads/ad-source-link";
import { buildJobPosting } from "@/lib/job-ads/job-posting-json-ld";
import { formatAdDescription } from "./format-ad-description";
import { JobAdDetailMeta } from "./job-ad-detail-meta";
import { StripBounceMarker } from "./strip-bounce-marker";
import contactLinkStyles from "./recruiter-contact-link.module.css";

/**
 * The anonymous job page (ADR 0165): one listed ad, as the server sends it. It takes `PublicJobAdDto` and can render
 * nothing else, so it cannot show the signed-in person's actions. The whole description is in the first response
 * (no excerpt, no script), as text through `formatAdDescription`, never as HTML (ADR 0160 D9.6). `status="Active"`
 * is a fact: a listed ad is Active by the publication predicate (ADR 0160 D2).
 *
 * The facts row under the dates (ADR 0171) shows the kommun and län as the snapshot names them and the employment
 * type and extent as the locale catalogue names them; an id the catalogue does not know is left out. The
 * `JobPosting` data block (`buildJobPosting`) asserts nothing this row and the rest of the page do not show, and is
 * absent when the ad is not complete enough for it; the page stays indexable either way.
 *
 * The link out appears twice: a secondary action under the dates and the one primary action in the footer. A long ad
 * puts the footer screens down the page, and a visitor from a search result should see where to apply without scrolling.
 */
export function PublicJobAd({ jobAd }: { jobAd: PublicJobAdDto }) {
  const tPublic = useTranslations("publicjob");
  const tUi = useTranslations("jobads.ui");
  const tEnum = useTranslations("jobads.enums");
  const link = adSourceLink(jobAd.url);

  const location = [jobAd.municipalityName, jobAd.regionName]
    .filter((name): name is string => name !== null && name.trim() !== "")
    .join(", ");
  const employment = [jobAd.employmentTypeConceptId, jobAd.worktimeExtentConceptId]
    .filter((id): id is CodedTaxonomyId => id !== null && isCodedTaxonomyId(id))
    .map((id) => codedTaxonomyName(tEnum, id, id))
    .join(" · ");
  const posting = buildJobPosting(jobAd);

  return (
    <main id="main" tabIndex={-1} className="jp-container jp-page focus:outline-none">
      <StripBounceMarker />
      {posting !== null && <JsonLdScript data={posting} />}
      <article className="jp-modal jp-modal--page jp-modal--public">
        <header className="jp-modal__head">
          <div className="min-w-0 flex-1">
            <h1 className="jp-modal__title">{jobAd.title}</h1>
            <p className="jp-modal__company">{jobAd.companyName}</p>
            <JobAdDetailMeta
              jobAd={{ status: "Active", publishedAt: jobAd.publishedAt, expiresAt: jobAd.expiresAt }}
            />
            {(location !== "" || employment !== "") && (
              <div className="jp-modal__meta jp-modal__facts">
                {location !== "" && (
                  <span className="jp-modal__fact">
                    <MapPin size={14} className="jp-modal__fact-icon" aria-hidden="true" />
                    <span>
                      <span className="sr-only">{tPublic("facts.location")} </span>
                      {location}
                    </span>
                  </span>
                )}
                {employment !== "" && (
                  <span className="jp-modal__fact">
                    <FileText size={14} className="jp-modal__fact-icon" aria-hidden="true" />
                    <span>
                      <span className="sr-only">{tPublic("facts.employment")} </span>
                      {employment}
                    </span>
                  </span>
                )}
              </div>
            )}
            {link && (
              <p className="mt-4">
                <a href={link.href} target="_blank" rel="noopener noreferrer" className="jp-btn jp-btn--secondary">
                  <ExternalLink size={14} aria-hidden="true" /> {tPublic(link.label)}
                </a>
              </p>
            )}
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
