import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getApplicationHistory } from "@/lib/api/application-history";
import { ApplicationHistoryList } from "@/components/application-history/application-history-list";
import { ForetagPagehero } from "@/components/foretag/foretag-pagehero";
import { ForetagSubnav } from "@/components/foretag/foretag-subnav";
import { renderSection } from "@/components/foretag/foretag-section";

/**
 * `/foretag/historik` (S1 #996) — the Ansökningshistorik surface: the caller's application history
 * grouped by employer (#444's projection), each group carrying its `ApplicationCount` inline. Its own
 * RECORD surface (ADR 0117), distinct from the follow/browse surfaces. org.nr is masked+flagged
 * backend-side (ADR 0087 D8(c)).
 */
export default async function AnsokningshistorikPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");

  const historyResult = await getApplicationHistory();

  return (
    <>
      <ForetagPagehero
        title={t("foretag.historyHeading")}
        lede={t("foretag.historyLede")}
      />
      <div className="jp-container jp-page">
        <ForetagSubnav active="historik" />
        {renderSection(historyResult, t, t("foretag.historyLoadErrorTitle"), (data) => (
          <ApplicationHistoryList items={data} />
        ))}
      </div>
    </>
  );
}
