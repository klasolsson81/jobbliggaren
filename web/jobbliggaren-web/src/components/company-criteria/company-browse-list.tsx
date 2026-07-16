import { useTranslations } from "next-intl";
import { ShieldAlert } from "lucide-react";
import { formatOrgNr } from "@/lib/company-follows/org-nr";
import type {
  CompanyBrowse,
  CriterionReference,
} from "@/lib/dto/company-criteria";

// How many SNI names to spell out before collapsing the rest to "+N".
const MAX_SNI_NAMES = 3;

interface CompanyBrowseListProps {
  readonly items: ReadonlyArray<CompanyBrowse>;
  readonly reference: CriterionReference;
}

/**
 * #560 PR-3 — the register-browse result table. A pure Server Component (no client JS): a flat
 * civic-utility ledger (`.jp-table`, no zebra, hairline rows). org.nr renders ONLY for an unmasked
 * legal entity; a personnummer-shaped sole-prop arrives masked (`organizationNumber: null` +
 * `isProtectedIdentity: true`, ADR 0087 D8(c)) and shows a "Skyddad identitet" badge, never a raw
 * number. The kommun column is the company's REGISTERED SEAT (säteskommun) — the page's help affordance
 * explains that it is not necessarily where the company operates. SNI codes resolve to Swedish names
 * via the reference tree (unknown codes fall back to the raw code).
 */
export function CompanyBrowseList({ items, reference }: CompanyBrowseListProps) {
  const t = useTranslations("pages.foretag.criteria.browse");

  // Leaf-code → Swedish name, built once for the whole table.
  const sniNameByCode = new Map<string, string>();
  for (const section of reference.sni) {
    for (const division of section.divisions) {
      for (const leaf of division.leaves) sniNameByCode.set(leaf.code, leaf.name);
    }
  }

  return (
    <div className="overflow-x-auto">
      <table className="jp-table w-full" aria-label={t("tableAria")}>
        <caption className="sr-only">{t("tableCaption")}</caption>
        <thead>
          <tr>
            <th scope="col">{t("colName")}</th>
            <th scope="col">{t("colOrgNr")}</th>
            <th scope="col">{t("colSeat")}</th>
            <th scope="col">{t("colSni")}</th>
          </tr>
        </thead>
        <tbody>
          {items.map((company, index) => (
            <tr key={company.organizationNumber ?? `${company.name}-${index}`} className="text-text-primary">
              <td className="text-text-primary">{company.name}</td>
              <td className="whitespace-nowrap font-mono text-text-secondary">
                {company.isProtectedIdentity ? (
                  <span className="inline-flex items-center gap-1 rounded-pill bg-warning-50 px-2 py-0.5 font-sans text-body-sm text-warning-700">
                    <ShieldAlert size={13} aria-hidden="true" />
                    {t("protectedIdentity")}
                  </span>
                ) : company.organizationNumber ? (
                  formatOrgNr(company.organizationNumber)
                ) : (
                  <span className="text-text-tertiary" aria-hidden="true">
                    –
                  </span>
                )}
              </td>
              <td className="whitespace-nowrap text-text-primary">
                {company.seatMunicipalityName ?? company.seatMunicipalityCode}{" "}
                <span className="font-mono text-body-sm text-text-secondary">
                  ({company.seatMunicipalityCode})
                </span>
              </td>
              <td className="text-text-primary">
                {(() => {
                  const { shown, extra } = resolveSniNames(company.sniCodes, sniNameByCode);
                  return extra > 0 ? `${shown} ${t("sniMore", { count: extra })}` : shown;
                })()}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/**
 * Spell out up to {@link MAX_SNI_NAMES} SNI names, collapsing the remainder to a `+N` count the caller
 * renders via i18n. Unknown codes (stale reference snapshot) fall back to the raw code so a value never
 * renders blank.
 */
function resolveSniNames(
  sniCodes: ReadonlyArray<string>,
  sniNameByCode: ReadonlyMap<string, string>,
): { shown: string; extra: number } {
  const names = sniCodes.map((code) => sniNameByCode.get(code) ?? code);
  return {
    shown: names.slice(0, MAX_SNI_NAMES).join(", "),
    extra: Math.max(0, names.length - MAX_SNI_NAMES),
  };
}
