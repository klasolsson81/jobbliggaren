import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

import type { Metadata } from "next";
import { notFoundMetadata } from "@/lib/metadata/not-found-title";

export async function generateMetadata(): Promise<Metadata> {
  return notFoundMetadata();
}

/**
 * /cv/ny — skapa ett CV från grunden, RETIRED (deferrad, inte raderad — #1061,
 * Klas live-verifiering 2026-07-25: "Detta är funktioner som inte ska vara med
 * i MVP"). Denna route behålls och returnerar 404 på route-nivå, så en gissad,
 * bokmärkt eller autocompletead URL inte kan nå ett fungerande skapa-formulär.
 *
 * Medvetet notFound(), INTE permanentRedirect: skapa-vägen är deferrad, inte
 * ersatt — ingenting tar över dess funktion, så en 308 hade påstått en flytt
 * som aldrig skett OCH cachats permanent av webbläsare, vilket låst ute
 * besökare även efter att vägen återvänder. Samma mekanism och samma skäl som
 * `cv/[id]/mall/page.tsx` (mallbyggaren) och
 * `cv/granska/[parsedId]/forbattra/page.tsx` (åtgärda-lagret).
 * `cv/granska/[parsedId]/komplettera/page.tsx` använder också `notFound()`.
 * Den skiljer sig i SKÄL, inte i mekanism: Slutför-guiden ersatte den genuint,
 * och 308:an avvisades även där eftersom målet självt är en 404.
 *
 * Session-grinden körs FÖRE 404:n: en utloggad besökare landar på /logga-in,
 * aldrig på en 404 som avslöjar att routen finns. Route-existens är ingen
 * auth-orakel åt något håll.
 *
 * VIKTIGT om scope: detta är INTE ADR 0112 verkställd konsekvent. ADR 0112
 * retirerade MALLbyggaren, ACT-lagret och Fas C, och nämner varken /cv/ny eller
 * CreateResumeCommand (mätt 2026-08-17: noll träffar). #1061 UTVIDGAR alltså
 * deferralen till skapa-från-grunden — ett nytt scope-beslut, recordat som ett
 * amendment till ADR 0112. ADR:n är gitignorerad (0071+), så denna kommentar är
 * den beständiga, spårade posten och är skriven för att stå själv.
 *
 * Kvar i trädet, inert och orört (ADR 0112 §Mechanism 1 — billig återgång slår
 * städning): `components/resumes/create-resume-form.tsx` med sitt enhetstest,
 * `lib/actions/resumes.ts:createResumeAction`, i18n-nycklarna `pages.cv.new.*`,
 * `pages.cv.newCv` och `pages.cv.emptyCreateFirst`. Backend-ytan
 * (`POST /api/v1/resumes` → `CreateResumeCommand`) blir onåbar av denna ändring
 * och retireras i en EGEN PR (annan change-reason, annan lane, andra
 * obligatoriska agenter): #1371.
 *
 * ORÖRT: /cv/granska (granskaren är produkten efter pivoten, ADR 0112).
 * /cv/[id] (redigering av ett sparat CV) var en öppen fråga hos Klas när denna
 * fil skrevs; han svarade 2026-08-17 att även redigeringen pausas, och routen är
 * grindad med samma mekanism av #1373. Radering och namnbyte låg på den sidan och
 * flyttade till CV-kortet i stället för att strandas — se den routens
 * doc-kommentar för Art. 7(3)-grunden.
 */
export default async function NyCvPage() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  notFound();
}
