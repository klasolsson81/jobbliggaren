import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import {
  getCompanyWatchCriteria,
  getCriterionReference,
} from "@/lib/api/company-criteria";
import type { CriterionReference } from "@/lib/dto/company-criteria";
import { CriteriaSection } from "@/components/company-criteria/criteria-section";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { renderSection } from "@/components/foretag/foretag-section";

// A degraded reference load must not fail the whole page (parity with the F4b taxonomy degradation):
// an empty tree makes the picker show civil "unavailable" notices and disables creating.
const EMPTY_CRITERION_REFERENCE: CriterionReference = {
  sniVersion: "",
  kommunVersion: "",
  sni: [],
  lan: [],
};

/**
 * `/foretag/smarta-bevakningar` (S1 #996) — the Smarta bevakningar surface: the user's saved
 * SNI/kommun searches, each yielding a company list to browse (detail at
 * `/foretag/smarta-bevakningar/[id]`) and follow FROM. A browsing/discovery surface, a sibling of Sök
 * företag — it sends NO per-company notices (ADR 0117). The slug carries the full disambiguating noun
 * (never a bare `bevakningar`, which would collide with Bevakade företag in the URL bar).
 */
export default async function SmartaBevakningarPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  const [criteriaResult, referenceResult] = await Promise.all([
    getCompanyWatchCriteria(),
    getCriterionReference(),
  ]);

  const criterionReference =
    referenceResult.kind === "ok" ? referenceResult.data : EMPTY_CRITERION_REFERENCE;

  return (
    <>
      <ForetagPagehero
        title={t("foretag.criteria.heading")}
        lede={t("foretag.criteria.lede")}
      />
      <div className="jp-container jp-page">
        <ForetagSubnav active="smartaBevakningar" />
        {renderSection(
          criteriaResult,
          t,
          t("foretag.criteria.loadErrorTitle"),
          (data) => <CriteriaSection items={data} reference={criterionReference} />,
        )}
      </div>
    </>
  );
}
