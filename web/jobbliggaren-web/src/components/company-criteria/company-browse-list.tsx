import { useTranslations } from "next-intl";
import { ShieldAlert } from "lucide-react";
import { formatOrgNr } from "@/lib/company-follows/org-nr";
import { CompanyFollowButton } from "@/components/company-follows/company-follow-button";
import type {
  CompanyBrowse,
  CriterionReference,
} from "@/lib/dto/company-criteria";

// How many SNI names to spell out before collapsing the rest to "+N".
const MAX_SNI_NAMES = 3;

interface CompanyBrowseListProps {
  readonly items: ReadonlyArray<CompanyBrowse>;
  readonly reference: CriterionReference;
  /**
   * #560 PR-C — the current user's follow-state per org.nr (companyWatchId, or null when not followed),
   * for the /foretag/sok "Bevaka"-per-row overlay. Composed at the RSC edge from a SEPARATE
   * company_watches read (never a server-side join against company_register — DPIA C-D4/M-C5). When
   * OMITTED the follow column is not rendered at all, so the criterion-run browse (bevakningar/[id]),
   * the other consumer, is unchanged. Masked/sole-prop rows (no org.nr key) are never followable.
   */
  readonly followStateByOrgNr?: ReadonlyMap<string, string | null>;
  /**
   * The table's accessible name + caption. The default strings belong to the criterion-run browse
   * ("Företag som matchar bevakningen"), which is FALSE on `/foretag/sok` — that surface answers a
   * search, not a bevakning, and a screen reader was hearing the wrong context on every result table.
   * Overridden there; omitted by `bevakningar/[id]`, which keeps its own wording.
   */
  readonly labels?: { readonly tableAria: string; readonly tableCaption: string };
}

/**
 * #560 PR-3 — the register-browse result table. A Server Component (a flat civic-utility ledger:
 * `.jp-table`, no zebra, hairline rows); when `followStateByOrgNr` is provided (#560 PR-C) it renders one
 * `CompanyFollowButton` client island per non-masked row. org.nr renders ONLY for an unmasked
 * legal entity; a personnummer-shaped sole-prop arrives masked (`organizationNumber: null` +
 * `isProtectedIdentity: true`, ADR 0087 D8(c)) and shows a "Skyddad identitet" badge, never a raw
 * number. The kommun column is the company's REGISTERED SEAT (säteskommun) — the page's help affordance
 * explains that it is not necessarily where the company operates. SNI codes resolve to Swedish names
 * via the reference tree (unknown codes fall back to the raw code).
 */
export function CompanyBrowseList({
  items,
  reference,
  followStateByOrgNr,
  labels,
}: CompanyBrowseListProps) {
  const t = useTranslations("pages.foretag.criteria.browse");
  const showFollow = followStateByOrgNr !== undefined;
  const tableAria = labels?.tableAria ?? t("tableAria");
  const tableCaption = labels?.tableCaption ?? t("tableCaption");

  // Leaf-code → Swedish name, built once for the whole table.
  const sniNameByCode = new Map<string, string>();
  for (const section of reference.sni) {
    for (const division of section.divisions) {
      for (const leaf of division.leaves) sniNameByCode.set(leaf.code, leaf.name);
    }
  }

  return (
    <div className="overflow-x-auto">
      <table
        // Both class strings are written out in full because an INTERPOLATED name is UNDECIDABLE to
        // the CSS guard in both directions — not, as an earlier version of this comment had it,
        // because it looks dead. Forward, `guard-css.mjs` skips any fragment followed by `${` and
        // counts it as a dynamic prefix; inverse, that same prefix SHIELDS every class behind it
        // from the dead-CSS sweep, deliberately, because that direction fails dangerously (#1065).
        // So `jp-x--${flag}` does not fail the gate — it silences it. Composition is fine: the guard
        // reads every string literal inside `className={...}` whatever helper joins them, so
        // `cn("jp-table jp-companyBrowse", showFollow && "jp-companyBrowse--withFollow")` would be
        // just as visible. Interpolation is the only form that costs coverage.
        className={
          showFollow
            ? "jp-table jp-companyBrowse jp-companyBrowse--withFollow w-full"
            : "jp-table jp-companyBrowse w-full"
        }
        aria-label={tableAria}
      >
        <caption className="sr-only">{tableCaption}</caption>
        {/* The column geometry is DECLARED here, not left to whatever rows loaded. /foretag/sok
            renders this table twice — the org.nr answer above the browse — and under auto layout a
            one-row answer and a fifty-row browse each sized their own columns, so the two disagreed
            about where Org.nr, Säteskommun and Branscher begin (51px apart, measured at 1280/1920/
            3440). Widths live in globals.css with the measurement behind each one.

            This colgroup must stay in lockstep with the <th> row below it: a column added to one
            and not the other silently shifts every column after it. `company-browse-list.test.tsx`
            pins the two counts against each other. */}
        <colgroup>
          {/* guard-allow: intentionally width-less — the company name absorbs the remainder, so the
              widths always sum exactly. The class is kept so the colgroup reads in column order. */}
          <col className="jp-companyBrowse__col--name" />
          <col className="jp-companyBrowse__col--orgnr" />
          <col className="jp-companyBrowse__col--seat" />
          <col className="jp-companyBrowse__col--sni" />
          {showFollow && <col className="jp-companyBrowse__col--follow" />}
        </colgroup>
        <thead>
          <tr>
            <th scope="col">{t("colName")}</th>
            <th scope="col">{t("colOrgNr")}</th>
            <th scope="col">{t("colSeat")}</th>
            <th scope="col">{t("colSni")}</th>
            {showFollow && <th scope="col">{t("colFollow")}</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((company, index) => (
            <tr key={company.organizationNumber ?? `${company.name}-${index}`} className="text-text-primary">
              {/* `wrap-break-word` breaks a token only when it cannot fit the column at all. The
                  register's longest unbreakable token is 42 characters (max token length over all
                  1 066 938 `company_name` values, split on space/slash/hyphen) — roughly 325px,
                  extrapolated from the measured 232px/29-char SNI token with `.jp-table td`'s 24px
                  padding held constant rather than scaled, not measured directly. That fits this
                  column on the 1136px rail but not at the table's minimum width, and under fixed
                  layout an over-long token overflows into Org.nr rather than widening anything. */}
              <td className="wrap-break-word text-text-primary">{company.name}</td>
              {/* `whitespace-nowrap` is scoped to the NUMBER, not to the cell. A formatted org.nr must
                  never break across lines; the "Skyddad identitet" badge is prose and may. Measured at
                  160px (sv) and 161px (en) against a 175px column — narrow enough headroom that a font
                  fallback could exceed it, and under fixed layout the cell would then overflow into
                  Säteskommun rather than grow. Letting the badge wrap removes that failure mode. */}
              <td className="font-mono text-text-secondary">
                {company.isProtectedIdentity ? (
                  <span className="inline-flex items-center gap-1 rounded-pill bg-warning-50 px-2 py-0.5 font-sans text-body-sm text-warning-700">
                    <ShieldAlert size={13} aria-hidden="true" />
                    {t("protectedIdentity")}
                  </span>
                ) : company.organizationNumber ? (
                  <span className="whitespace-nowrap">{formatOrgNr(company.organizationNumber)}</span>
                ) : (
                  <span className="text-text-tertiary" aria-hidden="true">
                    –
                  </span>
                )}
              </td>
              {/* The SCB kommun CODE is not rendered: Swedish kommun names are unique, so it
                  disambiguates nothing, and mono type is reserved for signal (DESIGN.md rule 4). The
                  code is the FALLBACK only — a row never renders blank when the name is missing.

                  `whitespace-nowrap` is GONE, and the mechanism is not the one it looks like: under
                  fixed layout cell content can never widen a column (CSS 2.1 §17.5.2.1 — the width
                  comes from the <col>), so nowrap would not have grown this column to fit "Ej svensk
                  hemortskommun". It would have OVERFLOWED it — 203px of unbroken text painted across
                  Branscher, on 23 837 rows. The choice the width makes is the real one: declaring
                  203px would tax every page 58px of mostly empty column for 2.2% of the register, so
                  the column is sized for the longest real kommun name instead ("Skinnskatteberg",
                  132px) and that one outlier wraps to two lines. `wrap-break-word` covers the case a
                  single kommun token still cannot fit. */}
              <td className="wrap-break-word text-text-primary">
                {company.seatMunicipalityName ?? company.seatMunicipalityCode}
              </td>
              <td className="text-text-primary">
                {(() => {
                  const { shown, extra } = resolveSniNames(company.sniCodes, sniNameByCode);
                  return extra > 0 ? `${shown} ${t("sniMore", { count: extra })}` : shown;
                })()}
              </td>
              {showFollow && (
                // No `whitespace-nowrap` on the CELL. `white-space` inherits, and this cell can hold
                // more than the button: on a failed follow `CompanyFollowButton` renders an error
                // sibling ("Kunde inte bevaka företaget. Försök igen.", ~230px). Under auto layout the
                // column grew to fit it; under fixed layout it cannot, so an inherited nowrap would
                // paint that sentence across the table edge. The button carries its own nowrap
                // instead, which is the only thing here that must not break. (The second half of
                // that failure — the error dragging the button's width with it — is fixed in
                // `CompanyFollowButton` itself, which no longer stretch-aligns the two.)
                <td>
                  {company.organizationNumber && !company.isProtectedIdentity ? (
                    <CompanyFollowButton
                      orgNr={company.organizationNumber}
                      companyName={company.name}
                      initialCompanyWatchId={
                        followStateByOrgNr?.get(company.organizationNumber) ?? null
                      }
                    />
                  ) : (
                    // Masked/sole-prop rows carry no org.nr key → not followable (ADR 0087 D8(c)). A
                    // screen-reader hears "Kan inte bevakas"; the dash is decorative for sighted users.
                    <>
                      <span className="sr-only">{t("cannotFollow")}</span>
                      <span className="text-text-tertiary" aria-hidden="true">
                        –
                      </span>
                    </>
                  )}
                </td>
              )}
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
