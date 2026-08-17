import { notFound, redirect } from "next/navigation";
import { getServerSession } from "@/lib/auth/session";

/**
 * Intercepting Route för @modal-slotten, `(.)cv/ny` — RETIRED tillsammans med
 * fullsidan (#1061). Den visade `CreateResumeForm` som modal vid soft-nav från
 * /cv; skapa-från-grunden är deferrad, så det finns ingenting kvar att fånga.
 *
 * Grindad, inte raderad, av två skäl. (1) En URL ska ha ETT beteende: om bara
 * fullsidan grindades skulle /cv/ny svara 404 vid hard-nav men rendera ett
 * fungerande formulär vid soft-nav. (2) Intercepten är i dag onåbar — den
 * fyrar bara på klient-navigering, och båda `<Link href="/cv/ny">` är borta —
 * men den skulle åter-armeras tyst i samma sekund någon lägger till en länk
 * eller ett `router.push`. En grind här gör den fällan omöjlig.
 *
 * Samma mekanism som fullsidan: session-grind FÖRST, sedan notFound(), aldrig
 * permanentRedirect. Motiveringen i sin helhet står i `(app)/cv/ny/page.tsx`,
 * som är den route en gissad URL faktiskt träffar. Hela ändringen är ett
 * `git revert`-mål om skapa-vägen återvänder (ADR 0112 §Mechanism 1 — därför
 * ingen feature-flagga).
 *
 * `RouteModalShell` och `CreateResumeForm` ligger kvar orörda i trädet.
 */
export default async function InterceptedCvNyModal() {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  notFound();
}
