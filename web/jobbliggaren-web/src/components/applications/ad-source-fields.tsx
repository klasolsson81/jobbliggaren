"use client";

import { useFormatter, useTranslations } from "next-intl";
import { formatDate } from "@/lib/i18n/format";
import { CopyTextButton } from "@/components/common/copy-text-button";
import type { ApplicationDto } from "@/lib/dto/applications";

/** Where the card's ad link comes from — said out loud, because the link is proof (#2248). */
type Provenance = "live" | "saved" | "manual";

function provenanceOf(jobAd: ApplicationDto["jobAd"]): Provenance | null {
  if (jobAd == null) return null;
  if (jobAd.source === "Manual") return "manual";
  // An erased ad rides the wire with the URL its snapshot froze at apply-time (#892).
  return jobAd.status === "Erased" ? "saved" : "live";
}

/** Only an http(s) URL becomes a link: a manual posting's URL is the user's own input. */
function safeHref(url: string): string | null {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.href : null;
  } catch {
    return null;
  }
}

/** The URL as the card shows it: no scheme, no leading "www.", nothing else changed. */
function displayUrl(url: string): string {
  return url.replace(/^https?:\/\//i, "").replace(/^www\./i, "");
}

/**
 * The card's labelled facts for an a-kassa report (#2248): Ort, the date applied, and the ad link
 * with its own copy button. The URL itself is the link text, so what is opened and what is copied
 * is visible on the card. The link and the button sit above the row link's overlay
 * (`.jp-app__adlink`), so neither opens the progress modal; the rest of the card still does.
 * A missing value is shown as missing ("Saknas i annonsen", "Ingen annonslänk sparad") — never
 * hidden and never guessed.
 */
export function AdSourceFields({
  application,
  title,
}: {
  application: ApplicationDto;
  title: string;
}) {
  const t = useTranslations("applications.ui.row.fields");
  const format = useFormatter();

  const location = application.location ?? null;
  const applied = formatDate(format, application.appliedAt);
  const jobAd = application.jobAd ?? null;
  const url = jobAd?.url ? jobAd.url : null;
  const href = url != null ? safeHref(url) : null;
  const provenance = provenanceOf(jobAd);
  const source = jobAd?.source ?? "";

  const caption =
    provenance === "manual"
      ? t("sourceManual")
      : provenance === "saved"
        ? t("sourceSaved", { source })
        : t("sourceLive", { source });

  return (
    <dl className="jp-app__fields">
      <dt>{t("location")}</dt>
      <dd data-missing={location == null || undefined}>{location ?? t("locationMissing")}</dd>

      <dt>{t("applied")}</dt>
      <dd data-missing={applied == null || undefined}>{applied ?? t("notApplied")}</dd>

      <dt>{t("ad")}</dt>
      {url == null ? (
        <dd data-missing>{t("adMissing")}</dd>
      ) : (
        <dd>
          <div className="jp-app__adlink">
            {href != null ? (
              <a
                href={href}
                target="_blank"
                rel="noopener noreferrer"
                title={url}
                aria-label={t("openAria", { title })}
                className="jp-app__adurl"
              >
                {displayUrl(url)}
              </a>
            ) : (
              <span className="jp-app__adurl" title={url}>
                {url}
              </span>
            )}
            <CopyTextButton
              value={url}
              ariaLabel={t("copyAria", { title })}
              label={t("copy")}
              copiedLabel={t("copied")}
              announcement={t("copiedAnnounced")}
              className="jp-app__copybtn shrink-0"
            />
          </div>
          <div className="jp-app__adcaption">{caption}</div>
        </dd>
      )}
    </dl>
  );
}
