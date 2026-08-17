// "use client": useActionState (React 19 form-state-hook) kräver en klient-ö.
// Formuläret är medvetet rubrik-löst: det hade två värdar som renderade var sin
// egen titel (`/cv/ny` en page-h1, @modal/(.)cv/ny en shell-header), så samma
// form kunde återanvändas i båda utan dubbel rubrik (DRY, ADR 0053).
"use client";

import Link from "next/link";
import { useActionState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { createResumeAction } from "@/lib/actions/resumes";

/**
 * CreateResumeForm — fälten för att skapa ett nytt CV.
 *
 * ⚠ MOTHBALLAD sedan #1061: skapa-från-grunden är deferrad ur MVP:n, och båda
 * värdarna (`/cv/ny` och @modal/(.)cv/ny) är grindade till 404. Komponenten har
 * ingen nåbar renderare kvar och ligger kvar orörd med flit — ADR 0112
 * §Mechanism 1, billig återgång framför städning. Motiveringen i sin helhet
 * står i `app/(app)/cv/ny/page.tsx`.
 *
 * `createResumeAction` redirectade till /cv/{id} vid 201. Inga
 * placeholder-exempel i fälten (Klas hård regel) — hjälptexten under labeln
 * bär instruktionen.
 */
export function CreateResumeForm() {
  const t = useTranslations("resumes");
  const [state, formAction, isPending] = useActionState(
    createResumeAction,
    null
  );

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cv-name">{t("createForm.nameLabel")}</Label>
        <p id="cv-name-help" className="text-body-sm text-text-primary">
          {t("createForm.nameHelp")}
        </p>
        <Input
          id="cv-name"
          name="name"
          required
          maxLength={200}
          disabled={isPending}
          aria-describedby="cv-name-help"
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="cv-fullname">{t("createForm.fullNameLabel")}</Label>
        <p id="cv-fullname-help" className="text-body-sm text-text-primary">
          {t("createForm.fullNameHelp")}
        </p>
        <Input
          id="cv-fullname"
          name="fullName"
          required
          maxLength={200}
          disabled={isPending}
          aria-describedby="cv-fullname-help"
        />
      </div>

      {state && !state.success && (
        <p role="alert" className="text-body-sm text-danger-700">
          {state.error}
        </p>
      )}

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={isPending}>
          {isPending ? t("createForm.submitPending") : t("createForm.submit")}
        </Button>
        <Button asChild variant="ghost">
          <Link href="/cv">{t("createForm.cancel")}</Link>
        </Button>
      </div>
    </form>
  );
}
