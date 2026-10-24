import type { JobAdShareData } from "@/lib/job-ads/share-job-ad";
import type { ShareTarget } from "@/lib/job-ads/share-job-ad-links";
import { ShareJobAdControls } from "./share-job-ad-controls";

export interface ShareJobAdLabels {
  readonly heading: string;
  /** Read before each target's name by a screen reader: "Dela via Facebook". */
  readonly via: string;
  /** Read after a platform's name by a screen reader: the link opens a new tab. */
  readonly newTab: string;
  readonly targets: Readonly<Record<ShareTarget, string>>;
  readonly copy: string;
  readonly copied: string;
  readonly more: string;
  readonly field: string;
  readonly copyFailed: string;
  readonly shareFailed: string;
}

/**
 * "Dela annonsen" (ADR 0183): a visible row of ways to pass a listed ad on, after the ad text. It takes props only, no
 * hooks and no translations, so the public page and the signed-in modal render the same row from their own labels.
 * The row is `data-nosnippet`: a search result quotes the ad, never its controls.
 */
export function ShareJobAd({
  data,
  labels,
  headingId,
  headingLevel = 2,
}: {
  data: JobAdShareData;
  labels: ShareJobAdLabels;
  headingId: string;
  headingLevel?: 2 | 3;
}) {
  const Heading = headingLevel === 2 ? "h2" : "h3";
  return (
    <section aria-labelledby={headingId} data-nosnippet="">
      <Heading id={headingId} className="jp-eyebrow mb-2">
        {labels.heading}
      </Heading>
      <ShareJobAdControls data={data} labels={labels} />
    </section>
  );
}
