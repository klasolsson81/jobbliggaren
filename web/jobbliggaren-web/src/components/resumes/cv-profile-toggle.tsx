import Link from "next/link";
import { useTranslations } from "next-intl";
import type { RenderProfile } from "@/lib/dto/parsed-resume";

/**
 * Profil-växel (F4-9). RSC, searchParam-driven — två `<Link>`:ar styled som
 * `.jp-segment` ger en server-re-render utan klient-JS. `aria-current` +
 * `data-active` markerar vald profil. Hrefs är fulla sökvägar så delning/
 * bokmärke bevarar profilvalet.
 *
 * `basePath` styr vilken route växeln länkar inom (default granska-vyn). Förbättra-
 * vyn (F4-10) återanvänder samma växel med sin egen basePath — inte en fork.
 */

const OPTIONS: ReadonlyArray<{
  value: RenderProfile;
  labelKey: "ats" | "visual";
}> = [
  { value: "Ats", labelKey: "ats" },
  { value: "Visual", labelKey: "visual" },
];

export function CvProfileToggle({
  parsedId,
  profile,
  basePath,
}: {
  parsedId: string;
  profile: RenderProfile;
  /** Route-bas växeln länkar inom. Default: granska-vyn (`/cv/granska/{id}`). */
  basePath?: string;
}) {
  const t = useTranslations("resumes.profileToggle");
  const base = basePath ?? `/cv/granska/${parsedId}`;
  return (
    <div role="group" aria-label={t("groupLabel")} className="jp-segment">
      {OPTIONS.map((option) => {
        const isActive = option.value === profile;
        return (
          <Link
            key={option.value}
            href={`${base}?profile=${option.value}`}
            className="jp-segment__opt"
            data-active={isActive}
            aria-current={isActive ? "true" : undefined}
            scroll={false}
          >
            <span>{t(option.labelKey)}</span>
          </Link>
        );
      })}
    </div>
  );
}
