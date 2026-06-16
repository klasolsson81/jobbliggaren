import Link from "next/link";
import type { RenderProfile } from "@/lib/dto/parsed-resume";

/**
 * Profil-växel (F4-9). RSC, searchParam-driven — två `<Link>`:ar styled som
 * `.jp-segment` ger en server-re-render utan klient-JS. `aria-current` +
 * `data-active` markerar vald profil. Hrefs är fulla sökvägar så delning/
 * bokmärke bevarar profilvalet.
 */

const OPTIONS: ReadonlyArray<{ value: RenderProfile; label: string }> = [
  { value: "Ats", label: "ATS-profil" },
  { value: "Visual", label: "Visuell profil" },
];

export function CvProfileToggle({
  parsedId,
  profile,
}: {
  parsedId: string;
  profile: RenderProfile;
}) {
  return (
    <div
      role="group"
      aria-label="Välj granskningsprofil"
      className="jp-segment"
    >
      {OPTIONS.map((option) => {
        const isActive = option.value === profile;
        return (
          <Link
            key={option.value}
            href={`/cv/granska/${parsedId}?profile=${option.value}`}
            className="jp-segment__opt"
            data-active={isActive}
            aria-current={isActive ? "true" : undefined}
            scroll={false}
          >
            <span>{option.label}</span>
          </Link>
        );
      })}
    </div>
  );
}
