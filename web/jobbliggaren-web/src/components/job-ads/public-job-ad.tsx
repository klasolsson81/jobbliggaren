import { useTranslations } from "next-intl";
import { Briefcase, ExternalLink, FileText, MapPin } from "lucide-react";
import { InformationLink } from "@/components/information/InformationLink";
import informationStyles from "@/components/information/information.module.css";
import { JsonLdScript } from "@/components/seo/json-ld-script";
import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";
import type { PublicJobAdSummary } from "@/lib/dto/public-job-ad-catalogue";
import { type CodedTaxonomyId, codedTaxonomyName, isCodedTaxonomyId } from "@/lib/i18n/coded-taxonomy";
import { adSourceLink, applicationLinkHref } from "@/lib/job-ads/ad-source-link";
import { buildJobPosting } from "@/lib/job-ads/job-posting-json-ld";
import { LOGIN_ENTRY_PATH } from "@/lib/auth/login-paths";
import { publicJobAdPath } from "@/lib/job-ads/public-job-ad-url";
import { formatAdDescription } from "./format-ad-description";
import { JobAdDetailMeta } from "./job-ad-detail-meta";
import { JobAdSourceLink } from "./job-ad-source-link";
import { PublicJobAdList } from "./public-job-ad-list";
import { ShareJobAd } from "./share-job-ad";
import { StripBounceMarker } from "./strip-bounce-marker";
import { useShareJobAdLabels } from "./use-share-job-ad-labels";
import contactLinkStyles from "./recruiter-contact-link.module.css";

const APPLY_TITLE_ID = "jp-apply-title";
const SHARE_TITLE_ID = "jp-share-title";
const RELATED_TITLE_ID = "jp-related-title";

/**
 * The anonymous job page (ADR 0165): one listed ad, as the server sends it. It takes `PublicJobAdDto` and can render
 * nothing else, so it cannot show the signed-in person's actions. The whole description is in the first response
 * (no excerpt, no script), as text through `formatAdDescription`, never as HTML (ADR 0160 D9.6). `status="Active"`
 * is a fact: a listed ad is Active by the publication predicate (ADR 0160 D2).
 *
 * The facts row under the dates (ADR 0171) shows the kommun and län as the snapshot names them, the employment type
 * and extent as the locale catalogue names them, and last the occupation group as the snapshot names it (#2203); an id
 * the catalogue does not know, and a name that is null or blank, is left out. The snapshot names the group in Swedish
 * in every locale, so its value carries `lang="sv"` (WCAG 3.1.2). The `JobPosting` data block (`buildJobPosting`)
 * asserts nothing this row and the rest of the page do not show, and is absent when the ad is not complete enough for
 * it; the page stays indexable either way. It never carries the group.
 *
 * The page is where a shared link lands, for a visitor who is not signed in (ADR 0183). The head ends with the one primary
 * action, so a visitor from a search result or a shared link sees where to apply without scrolling. With the employer's
 * application page, as the backend's gate admits it (#2214 PR 1b), that is "Ansök", after the ad's own page as a text
 * link on its line (`JobAdSourceLink`, the signed-in card's two links, #2191). Without one it is the link out, once,
 * under "Sök jobbet". The share row follows the text, where the decision to pass the ad on is made. The
 * footer invites the visitor to sign in and come back to this ad: a plain link with `rel="nofollow"`, since every
 * listed ad would otherwise hand crawlers its own login URL.
 *
 * After the article, and outside it because the article is the employer's content, come other listed ads in the same
 * occupation group (ADR 0182) and then two links that explain how CV review and matching work, all in the column the
 * card has. The related rows are the catalogue's, under a heading that names no group and no count, in one
 * `data-nosnippet` section: a snippet of this ad quoting another ad's title or employer would be false and stale in a
 * copy we cannot reach (ADR 0166 D10 trigger 15). With no rows there is no section at all, and nothing on the page
 * depends on it. The explainer links carry the footer's own names for the same two pages (WCAG 3.2.4).
 */
export function PublicJobAd({
  jobAd,
  related = [],
}: {
  jobAd: PublicJobAdDto;
  /** The related rows; empty, or left out, renders no section. */
  related?: readonly PublicJobAdSummary[];
}) {
  const tPublic = useTranslations("publicjob");
  const tUi = useTranslations("jobads.ui");
  const tEnum = useTranslations("jobads.enums");
  const tLanding = useTranslations("landing");
  const shareLabels = useShareJobAdLabels();
  const link = adSourceLink(jobAd.url);
  const applyHref = applicationLinkHref(jobAd.applicationUrl);

  const location = [jobAd.municipalityName, jobAd.regionName]
    .filter((name): name is string => name !== null && name.trim() !== "")
    .join(", ");
  const employment = [jobAd.employmentTypeConceptId, jobAd.worktimeExtentConceptId]
    .filter((id): id is CodedTaxonomyId => id !== null && isCodedTaxonomyId(id))
    .map((id) => codedTaxonomyName(tEnum, id, id))
    .join(" · ");
  const occupation = jobAd.occupationGroupName?.trim() ?? "";
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
            {(location !== "" || employment !== "" || occupation !== "") && (
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
                {occupation !== "" && (
                  <span className="jp-modal__fact">
                    <Briefcase size={14} className="jp-modal__fact-icon" aria-hidden="true" />
                    <span>
                      <span className="sr-only">{tPublic("facts.occupation")} </span>
                      <span lang="sv">{occupation}</span>
                    </span>
                  </span>
                )}
              </div>
            )}
            {applyHref !== null ? (
              <>
                <JobAdSourceLink jobAd={{ url: jobAd.url, applicationUrl: applyHref }} />
                <div className="mt-4">
                  <a href={applyHref} target="_blank" rel="nofollow noopener noreferrer" className="jp-btn jp-btn--primary">
                    <ExternalLink size={14} aria-hidden="true" /> {tUi("detail.apply")}
                  </a>
                </div>
              </>
            ) : (
              link && (
                <section aria-labelledby={APPLY_TITLE_ID} className="mt-4">
                  <h2 id={APPLY_TITLE_ID} className="jp-eyebrow mb-2">
                    {tPublic("apply.heading")}
                  </h2>
                  <a href={link.href} target="_blank" rel="noopener noreferrer" className="jp-btn jp-btn--primary">
                    <ExternalLink size={14} aria-hidden="true" /> {tPublic(link.label)}
                  </a>
                </section>
              )
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
              {tPublic("recruiterNoticeLink")}
            </InformationLink>
          </p>

          <ShareJobAd jobAd={jobAd} labels={shareLabels} headingId={SHARE_TITLE_ID} />
        </div>

        <div className="jp-modal__foot jp-modal__foot--split" data-nosnippet="">
          <div className="min-w-[min(100%,20rem)] flex-1">
            <h2 className="text-h4 font-bold">
              {tPublic("signIn.heading")}
            </h2>
            <p className="mt-1">{tPublic("signIn.body")}</p>
          </div>
          <div className="jp-modal__footgroup jp-modal__footgroup--end">
            <a
              href={`${LOGIN_ENTRY_PATH}?next=${encodeURIComponent(publicJobAdPath(jobAd.id))}`}
              rel="nofollow"
              className="jp-btn jp-btn--secondary"
            >
              {tLanding("footer.start.auth")}
            </a>
          </div>
        </div>
      </article>

      <div className="mx-auto mt-10 w-full max-w-[760px]">
        {related.length > 0 && (
          <section aria-labelledby={RELATED_TITLE_ID} data-nosnippet="" className="mb-10">
            <h2 id={RELATED_TITLE_ID} className="jp-eyebrow">
              {tPublic("related.heading")}
            </h2>
            <PublicJobAdList rows={related} labelledBy={RELATED_TITLE_ID} headingLevel={3} idPrefix="related" />
          </section>
        )}
        <div>
          <h2 className="jp-eyebrow">{tPublic("explainers.heading")}</h2>
          <ul className="mt-2 flex flex-col gap-1">
            <li>
              <InformationLink
                id={`information-job-${jobAd.id}-cv-review`}
                href="/cv-granskning"
                className={`${informationStyles.entryLink} underline`}
              >
                {tLanding("common.cvReviewLink")}
              </InformationLink>
            </li>
            <li>
              <InformationLink
                id={`information-job-${jobAd.id}-matching`}
                href="/matchning"
                className={`${informationStyles.entryLink} underline`}
              >
                {tLanding("footer.support.howMatching")}
              </InformationLink>
            </li>
          </ul>
        </div>
      </div>
    </main>
  );
}
