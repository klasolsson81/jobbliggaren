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
 * Den kanoniska granska-vyn (Fas 4b PR-8.4) passerar bara `basePath` (ingen
 * parsedId finns), därför är `parsedId` valfri: när `basePath` ges är den oanvänd.
 *
 * `query` adds parameters that survive the switch (the CV review ledger's outcome filter, #2083);
 * everything else in the current URL is dropped, so a dimension never outlives its profile.
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
  query,
}: {
  /** Parse-artefaktens id. Valfri: bara den för default-basen. Utelämnas när
   * `basePath` ges (den kanoniska vyn har ingen parsedId). */
  parsedId?: string;
  profile: RenderProfile;
  /** Route-bas växeln länkar inom. Default: granska-vyn (`/cv/granska/{id}`). */
  basePath?: string;
  /** Parameters to carry across the switch, after `profile`. */
  query?: Readonly<Record<string, string>>;
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
            href={`${base}?${new URLSearchParams({ profile: option.value, ...query })}`}
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
