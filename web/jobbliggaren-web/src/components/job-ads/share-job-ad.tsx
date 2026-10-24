import type { PublicJobAdDto } from "@/lib/dto/public-job-ad";
import { publicJobAdTitle } from "@/lib/job-ads/public-job-ad-title";
import { publicJobAdUrl } from "@/lib/job-ads/public-job-ad-url";
import { type ShareTarget, shareTargetLinks } from "@/lib/job-ads/share-job-ad-links";
import { type ShareJobAdButtonLabels, ShareJobAdControls } from "./share-job-ad-controls";
import { SHARE_ITEM } from "./share-job-ad-styles";

export interface ShareJobAdLabels extends ShareJobAdButtonLabels {
  readonly heading: string;
  /** Read before each target's name by a screen reader: "Dela via Facebook". */
  readonly via: string;
  /** Read after a platform's name by a screen reader: the link opens a new tab. */
  readonly newTab: string;
  readonly targets: Readonly<Record<ShareTarget, string>>;
}

/**
 * "Dela annonsen" (ADR 0183): a visible row of ways to pass a listed ad on, after the ad text. The caller decides that
 * the ad is listed; this takes only its id, title and employer, and builds what is shared from them, so the canonical
 * link and the page title are the only values that reach the client island. The four links are rendered here, on the
 * server; the island adds the two buttons. The row is `data-nosnippet`: a search result quotes the ad, never its
 * controls.
 */
export function ShareJobAd({
  jobAd,
  labels,
  headingId,
  headingLevel = 2,
}: {
  jobAd: Pick<PublicJobAdDto, "id" | "title" | "companyName">;
  labels: ShareJobAdLabels;
  headingId: string;
  headingLevel?: 2 | 3;
}) {
  const data = { title: publicJobAdTitle(jobAd), url: publicJobAdUrl(jobAd.id) };
  const Heading = headingLevel === 2 ? "h2" : "h3";
  return (
    <section aria-labelledby={headingId} data-nosnippet="">
      <Heading id={headingId} className="jp-eyebrow mb-2">
        {labels.heading}
      </Heading>
      <ShareJobAdControls data={data} labels={labels}>
        {shareTargetLinks(data).map(({ target, href }) => (
          <li key={target}>
            {target === "email" ? (
              <a href={href} className={SHARE_ITEM}>
                <span className="sr-only">{labels.via}</span> {labels.targets[target]}
              </a>
            ) : (
              <a href={href} target="_blank" rel="nofollow noopener noreferrer" className={SHARE_ITEM}>
                <span className="sr-only">{labels.via}</span> {labels.targets[target]}{" "}
                <span className="sr-only">{labels.newTab}</span>
              </a>
            )}
          </li>
        ))}
      </ShareJobAdControls>
    </section>
  );
}
