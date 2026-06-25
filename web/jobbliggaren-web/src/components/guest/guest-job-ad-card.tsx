import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { jobSourceLabel } from "@/lib/job-ads/status";
import { formatDate } from "@/lib/i18n/format";
import type { GuestMockJobAd } from "@/lib/guest/mock-data";

// F-Pre Punkt 5b 2026-05-24 — gäst-variant av JobAdCard. Länk pekar mot
// `/gast/jobb/[id]` (gäst-tree-isolering — får aldrig länka till
// `/jobb/[id]` som är auth-gated). Återanvänder `.jp-job`-CSS-chassi
// (delad med live, HANDOVER §5.3) men utan JobTags-island (mockdata
// behöver inga NY/färskhet-tags som faller från BE).

export function GuestJobAdCard({ jobAd }: { jobAd: GuestMockJobAd }) {
  // Synchronous next-intl translators + locale-medveten formatter — håller
  // detta en non-async RSC. `t` bär enum-etiketten (jobSourceLabel), `tg` bär
  // gäst-kortets copy.
  const t = useTranslations("jobads.enums");
  const tg = useTranslations("guest");
  const format = useFormatter();
  const publishedAt = formatDate(format, jobAd.publishedAtIso) ?? "";
  const expiresAt = formatDate(format, jobAd.expiresAtIso);

  return (
    <Link
      href={`/gast/jobb/${jobAd.id}`}
      className="jp-job"
      aria-label={tg("jobb.cardAriaLabel", {
        title: jobAd.title,
        company: jobAd.companyName,
      })}
    >
      <div className="jp-job__body">
        <h3 className="jp-job__title">
          <span>{jobAd.title}</span>
        </h3>
        <div className="jp-job__company">{jobAd.companyName}</div>
        <div className="jp-job__meta">
          <span>{jobSourceLabel(t, jobAd.source)}</span>
          <span>
            {tg.rich("jobb.cardPublished", {
              date: publishedAt,
              b: (chunks) => <b>{chunks}</b>,
            })}
          </span>
          {expiresAt && (
            <span>
              {tg.rich("jobb.cardApplyBy", {
                date: expiresAt,
                b: (chunks) => <b>{chunks}</b>,
              })}
            </span>
          )}
        </div>
      </div>
    </Link>
  );
}
