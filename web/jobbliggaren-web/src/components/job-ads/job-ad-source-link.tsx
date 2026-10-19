import { useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import { STANDALONE_LINK } from "@/components/auth/mail-link";
import { adSourceLink } from "@/lib/job-ads/ad-source-link";
import type { JobAdDetailDto } from "@/lib/dto/job-ads";

/**
 * #2191 (ADR 0053 Amendment 2026-10-19) — the ad's own page at the source, on a line of its own under the
 * header's dates, when the footer's primary is "Ansök" to the employer's application page. The footer then
 * has no room for "Öppna annonsen" on one row, and the original ad must stay easy to find. Without an
 * application page the footer's "Öppna annonsen" is the way to the ad, so this renders nothing.
 */
export function JobAdSourceLink({
  jobAd,
}: {
  jobAd: Pick<JobAdDetailDto, "url" | "applicationUrl">;
}) {
  const t = useTranslations("jobads.ui.detail");
  if (!jobAd.applicationUrl) return null;

  const source = adSourceLink(jobAd.url);
  if (!source) return null;

  return (
    <p className="mt-2 text-body-sm">
      <a
        href={source.href}
        target="_blank"
        rel="noopener noreferrer"
        className={`${STANDALONE_LINK} inline-flex items-center gap-1.5 font-semibold`}
      >
        <ExternalLink size={14} aria-hidden="true" />
        {t(source.label === "openAdAf" ? "sourceAdPlatsbanken" : "sourceAd")}
      </a>
    </p>
  );
}
