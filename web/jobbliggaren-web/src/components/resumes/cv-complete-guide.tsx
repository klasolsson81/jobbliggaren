"use client";

// Client Component: Slutför-guiden är genomgående interaktiv (RHF + useFieldArray,
// stegat flöde med klient-only steg-state, Pågående-toggles, chip-editorer,
// bekräfta-inte-fyll-mönster, programmatisk fokus-flytt vid stegbyte + valideringsfel,
// och useTransition runt det enda save-anropet). CV-PII tas emot som props från RSC:n
// (server-only läsning) men redigeras här. Ingenting persisteras förrän Spara (D5a) —
// close-kontrollen bekräftar bort osparade ändringar, aldrig ett "spara utkast".

import { useMemo, useRef, useState, useTransition } from "react";
import {
  useForm,
  useFieldArray,
  Controller,
  type FieldPath,
  type UseFormReturn,
} from "react-hook-form";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { AlertTriangle, Check, Plus, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { StatusPill } from "@/components/ui/status-pill";
import { ToggleRow } from "@/components/ui/toggle-row";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from "@/components/ui/dialog";
import { makePromoteParsedResumeSchema } from "@/lib/actions/resume-schemas";
import { promoteParsedResumeFromGuideAction } from "@/lib/actions/resumes";
import {
  guidePathToFormPath,
  guidePathToStepAndElementId,
  GUIDE_STEP_DETAILS,
  GUIDE_STEP_EXPERIENCE,
  GUIDE_STEP_SKILLS,
  GUIDE_STEP_SAVE,
} from "@/lib/forms/resume-path-routing";
import { deriveGapSummaryFromForm } from "@/lib/resumes/gap-tasks";
import type {
  ParsedContentDto,
  ParseConfidenceDto,
  ParsedGapSummary,
} from "@/lib/dto/parsed-resume";
import type { ResumeContentDto } from "@/lib/types/resumes";

const STEP_COUNT = 4;
const CLOSE_HREF = "/cv";
const ERROR_ID = "cv-guide-error";

type ContactKey = "fullName" | "email" | "phone" | "location";

type ExperienceFormValue = {
  company: string;
  role: string;
  startDate: string;
  endDate: string;
  ongoing: boolean;
  description: string;
  // Display-only tolkad period från parsern. Ingår ALDRIG i payloaden.
  periodHint: string;
};

type EducationFormValue = {
  institution: string;
  degree: string;
  startDate: string;
  endDate: string;
  ongoing: boolean;
  // Display-only tolkad period från parsern. Ingår ALDRIG i payloaden.
  periodHint: string;
};

type SectionEntryFormValue = { title: string; body: string };
type SectionFormValue = { heading: string; entries: SectionEntryFormValue[] };

type FormValues = {
  name: string;
  personalInfo: {
    fullName: string;
    email: string;
    phone: string;
    location: string;
  };
  summary: string;
  experiences: ExperienceFormValue[];
  educations: EducationFormValue[];
  skills: Array<{ name: string }>;
  languages: Array<{ name: string }>;
  sections: SectionFormValue[];
};

interface CvCompleteGuideProps {
  parsedId: string;
  sourceFileName: string;
  content: ParsedContentDto;
  confidence: ParseConfidenceDto;
}

/**
 * Prefyller formen från den löst tolkade ParsedContentDto (null → tom sträng).
 * Parsern gissar ALDRIG datum (DQ3-3a) — alla strukturerade datum startar tomma.
 * `periodHint` bär den råa tolkade perioden som en civil ledtråd och strippas ur
 * payloaden. Språk prefyllas namn-only (proficiency sätts NotStated i payloaden —
 * aldrig syntetiserad). Fria sektioner ("Projekt", "Referenser") prefylls sedan #815 —
 * parsern producerar dem numera i stället för att svälja dem in i sammanfattningen.
 */
function toFormValues(name: string, content: ParsedContentDto): FormValues {
  return {
    name,
    personalInfo: {
      fullName: content.contact.fullName ?? "",
      email: content.contact.email ?? "",
      phone: content.contact.phone ?? "",
      location: content.contact.location ?? "",
    },
    summary: content.profile ?? "",
    experiences: content.experiences.map((e) => ({
      company: e.organization ?? "",
      role: e.title ?? "",
      startDate: "",
      endDate: "",
      ongoing: false,
      description: e.rawText ?? "",
      periodHint: e.period ?? "",
    })),
    educations: content.educations.map((e) => ({
      institution: e.institution ?? "",
      degree: e.degree ?? "",
      startDate: "",
      endDate: "",
      ongoing: false,
      periodHint: e.period ?? "",
    })),
    skills: content.skills.map((s) => ({ name: s })),
    languages: content.languages.map((l) => ({ name: l })),
    // #815: fria sektioner ("Projekt", "Referenser") prefylls nu. Tidigare producerade
    // parsern dem inte alls — de svaldes in i sammanfattningen — så fältet startade tomt
    // och användarens projektlista fanns bara som en textklump i profilen. Rubriken är
    // användarens egen, ordagrant; en post utan titel behåller sin tomma titel (parsern
    // hittar aldrig på en).
    sections: content.sections.map((section) => ({
      heading: section.heading,
      entries: section.entries.map((entry) => ({
        title: entry.title ?? "",
        body: entry.lines.join("\n"),
      })),
    })),
  };
}

// Råpayloaden matchar makeResumeContentSchema:s ingångsform. `periodHint`/`ongoing`
// och all display-only-state strippas. Pågående → endDate undefined.
type RawContentPayload = {
  personalInfo: {
    fullName: string;
    email?: string;
    phone?: string;
    location?: string;
  };
  experiences: Array<{
    company: string;
    role: string;
    startDate: string;
    endDate?: string;
    description?: string;
  }>;
  educations: Array<{
    institution: string;
    degree: string;
    startDate: string;
    endDate?: string;
  }>;
  skills: Array<{ name: string; yearsExperience: number | null }>;
  summary?: string;
  languages: Array<{ name: string; proficiency: "NotStated" }>;
  sections: Array<{
    heading: string;
    entries: Array<{ title: string; lines: string[] }>;
  }>;
};

/** Delar upp fritext-textarean i rader; tomma rader släpps (kosmetiskt, inom en post). */
function bodyToLines(body: string): string[] {
  return body
    .split("\n")
    .map((line) => line.trimEnd())
    .filter((line) => line.trim().length > 0);
}

function toRawPayload(values: FormValues): RawContentPayload {
  return {
    personalInfo: {
      fullName: values.personalInfo.fullName,
      email: values.personalInfo.email || undefined,
      phone: values.personalInfo.phone || undefined,
      location: values.personalInfo.location || undefined,
    },
    experiences: values.experiences.map((e) => ({
      company: e.company,
      role: e.role,
      startDate: e.startDate,
      // Pågående ON → inget slutdatum (payload endDate undefined).
      endDate: e.ongoing ? undefined : e.endDate || undefined,
      description: e.description || undefined,
    })),
    educations: values.educations.map((e) => ({
      institution: e.institution,
      degree: e.degree,
      startDate: e.startDate,
      endDate: e.ongoing ? undefined : e.endDate || undefined,
    })),
    // Guiden bär inga år per kompetens (yearsExperience null).
    skills: values.skills
      .filter((s) => s.name.trim().length > 0)
      .map((s) => ({ name: s.name, yearsExperience: null })),
    summary: values.summary || undefined,
    // Namn-only språk; proficiency NotStated (aldrig syntetiserad — användaren
    // sätter en riktig nivå senare, ADR 0095 D-C / OQ3).
    languages: values.languages
      .filter((l) => l.name.trim().length > 0)
      .map((l) => ({ name: l.name, proficiency: "NotStated" as const })),
    sections: values.sections.map((section) => ({
      heading: section.heading,
      entries: section.entries.map((entry) => ({
        title: entry.title,
        lines: bodyToLines(entry.body),
      })),
    })),
  };
}

const CONTACT_FIELDS: ReadonlyArray<{
  key: ContactKey;
  type?: string;
  optional: boolean;
}> = [
  { key: "fullName", optional: false },
  { key: "email", type: "email", optional: true },
  { key: "phone", type: "tel", optional: true },
  { key: "location", optional: true },
];

/** Vilka av de nio uppgifterna som bor på respektive steg (SSOT: gap-tasks). */
const STEP_TASK_KEYS: Record<number, ReadonlyArray<keyof ParsedGapSummary>> = {
  [GUIDE_STEP_DETAILS]: [
    "hasFullName",
    "hasEmail",
    "hasPhone",
    "hasLocation",
    "hasProfile",
  ],
  [GUIDE_STEP_EXPERIENCE]: ["hasExperience", "hasEducation"],
  [GUIDE_STEP_SKILLS]: ["hasSkills", "hasLanguages"],
};

type FieldError = { path: string | null; message: string };

const isPresent = (value: string | null): boolean =>
  value != null && value.trim().length > 0;

export function CvCompleteGuide({
  parsedId,
  sourceFileName,
  content,
  confidence,
}: CvCompleteGuideProps) {
  const router = useRouter();
  const t = useTranslations("validation");
  const tr = useTranslations("resumes.guide");
  const schema = useMemo(() => makePromoteParsedResumeSchema(t), [t]);

  const form = useForm<FormValues>({
    defaultValues: toFormValues(content.contact.fullName ?? "", content),
    shouldUnregister: false,
  });
  const { control, register, watch, formState } = form;
  // RHF:s formState är en Proxy: en egenskap prenumereras BARA om den läses under
  // render. Läs isDirty här (inte först i requestClose) — annars förblir den
  // frusen på false och Stäng-bekräftelsen blir död (test-writer-fynd PR-8.3).
  const { isDirty } = formState;

  const [step, setStep] = useState(GUIDE_STEP_DETAILS);
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<FieldError | null>(null);
  const [confirmClose, setConfirmClose] = useState(false);

  // Contact-fält som befordrats från bekräfta-läge till redigerbart (Ändra).
  const [expandedContact, setExpandedContact] = useState<ReadonlySet<ContactKey>>(
    new Set(),
  );
  // KOLLAPSADE erfarenhet/utbildning-poster (per useFieldArray-fältets id). De
  // parsade posterna seedas kollapsade vid första render (bekräfta-mönstret);
  // nya poster (append) saknas i mängden → renderas expanderade direkt.
  const [collapsedSeeded, setCollapsedSeeded] = useState(false);
  const [collapsedEntries, setCollapsedEntries] = useState<ReadonlySet<string>>(
    () => new Set(),
  );

  const titleRef = useRef<HTMLHeadingElement | null>(null);

  const experiences = useFieldArray({ control, name: "experiences" });
  const educations = useFieldArray({ control, name: "educations" });
  const skills = useFieldArray({ control, name: "skills" });
  const languages = useFieldArray({ control, name: "languages" });
  const sections = useFieldArray({ control, name: "sections" });

  // Render-tids-seed (set-state-in-effect-fri, samma mönster som match-setup):
  // fångar de parsade posternas fält-id på första render och kollapsar dem.
  if (!collapsedSeeded) {
    setCollapsedSeeded(true);
    const initial = new Set<string>();
    experiences.fields.forEach((field) => initial.add(field.id));
    educations.fields.forEach((field) => initial.add(field.id));
    setCollapsedEntries(initial);
  }

  // Live-värden → härledd gap-summering (SSOT gap-tasks, LÄSES bara — aldrig
  // omdefinierad här: hubbens mätare och guiden måste vara överens om vad en
  // uppgift ÄR). Driver "kvar"-räkningen på stegen och Spara-sammanställningen.
  const values = watch();
  const gaps = deriveGapSummaryFromForm(values);

  // Live-validering mot SAMMA schema som submit (och som servern). Ger stegen ett
  // ärligt blockerings-tillstånd: närvaro (gaps) och giltighet (issues) är två
  // olika påståenden, och den gamla indikatorn konflaterade dem — en appendad men
  // TOM post räknades som "ifylld" och steget blev grönbockat medan submit ändå
  // nekade (CTO-bind Q3/Q3-A).
  const liveIssuePathsByStep = useMemo(() => {
    const parsed = schema.safeParse({
      parsedResumeId: parsedId,
      name: values.name,
      content: toRawPayload(values),
    });
    const byStep = new Map<number, string[]>();
    if (parsed.success) return byStep;
    for (const issue of parsed.error.issues) {
      const path = issue.path.join(".");
      const target = guidePathToStepAndElementId(path);
      if (!target) continue;
      const bucket = byStep.get(target.step) ?? [];
      bucket.push(path);
      byStep.set(target.step, bucket);
    }
    return byStep;
  }, [schema, parsedId, values]);

  // Per-sektions parse-konfidens (ärlig proveniens från backend). Bara "Degraded"
  // ytas som en not; "NotFound" bärs redan av "Saknades i filen"-räkningen.
  const levelBySection = useMemo(
    () => new Map(confidence.sections.map((s) => [s.section, s.level])),
    [confidence.sections],
  );
  const isDegraded = (section: string) =>
    levelBySection.get(section) === "Degraded";

  function focusTitle() {
    // Flytta fokus till stegrubriken efter commit (WCAG 2.4.3).
    queueMicrotask(() => titleRef.current?.focus());
  }

  function goToStep(next: number) {
    setStep(next);
    focusTitle();
  }

  function expandContact(key: ContactKey) {
    setExpandedContact((prev) => new Set(prev).add(key));
  }

  function expandEntry(id: string) {
    setCollapsedEntries((prev) => {
      const next = new Set(prev);
      next.delete(id);
      return next;
    });
  }

  function remainingForStep(stepIndex: number): number {
    const keys = STEP_TASK_KEYS[stepIndex] ?? [];
    return keys.filter((key) => !gaps[key]).length;
  }

  function blockersForStep(stepIndex: number): number {
    return liveIssuePathsByStep.get(stepIndex)?.length ?? 0;
  }

  /**
   * Stegets ärliga tillstånd (CTO-bind Q3-A — tre tillstånd, aldrig två):
   *
   * - `attention` — steget äger minst ett valideringsfel: det HINDRAR sparande.
   * - `remaining` — inget hindrar, men uppgifter är ofyllda (gap-tasks).
   * - `done`      — inget hindrar OCH inget är ofyllt.
   *
   * Bocken påstår därmed exakt en sak: "allt på det här steget är ifyllt, och
   * inget här hindrar dig från att spara." Den kan inte längre sättas av en tom
   * post (som är ogiltig → `attention`) och inte heller av ett orört steg (som
   * har gaps kvar → `remaining`). Spara-sammanställningen läser samma predikat,
   * så railen och den kan inte säga emot varandra.
   *
   * Spara-steget äger inga gap-uppgifter och blir därför aldrig `done` — det är
   * `attention` när CV-namnet saknas, annars neutralt. Det faller ut ur modellen.
   */
  function stepState(stepIndex: number): "attention" | "remaining" | "done" {
    if (blockersForStep(stepIndex) > 0) return "attention";
    const keys = STEP_TASK_KEYS[stepIndex] ?? [];
    if (keys.length === 0) return "remaining";
    return keys.every((key) => gaps[key]) ? "done" : "remaining";
  }

  /**
   * Skärmläsar-texten för stegets tillstånd (WCAG 1.4.1 — glyfen bär aldrig
   * påståendet ensam). `null` när steget inte har något sant att säga: Spara-steget
   * äger inga uppgifter, så "0 uppgifter kvar" vore brus, inte information.
   */
  function stepStatusLabel(stepIndex: number): string | null {
    const state = stepState(stepIndex);
    if (state === "attention") {
      return tr("railStatusAttention", { count: blockersForStep(stepIndex) });
    }
    if (state === "done") return tr("railStatusDone");
    const remaining = remainingForStep(stepIndex);
    return remaining > 0 ? tr("railStatusRemaining", { count: remaining }) : null;
  }

  const CONTENT_STEPS = [
    GUIDE_STEP_DETAILS,
    GUIDE_STEP_EXPERIENCE,
    GUIDE_STEP_SKILLS,
  ];
  const hasBlockers = liveIssuePathsByStep.size > 0;
  const hasRemainingTasks = CONTENT_STEPS.some(
    (stepIndex) => remainingForStep(stepIndex) > 0,
  );

  function requestClose() {
    if (isDirty) {
      setConfirmClose(true);
      return;
    }
    router.push(CLOSE_HREF);
  }

  // Vid valideringsfel: hoppa till felets steg, expandera ev. kollapsad post/kontakt,
  // flytta fokus till fältet.
  function routeToError(path: string) {
    const target = guidePathToStepAndElementId(path);
    if (!target) return;
    setStep(target.step);

    const contact = path.match(/^content\.personalInfo\.(\w+)$/);
    if (contact) expandContact(contact[1] as ContactKey);

    const exp = path.match(/^content\.experiences\.(\d+)\./);
    if (exp) {
      const id = experiences.fields[Number(exp[1])]?.id;
      if (id) expandEntry(id);
    }
    const edu = path.match(/^content\.educations\.(\d+)\./);
    if (edu) {
      const id = educations.fields[Number(edu[1])]?.id;
      if (id) expandEntry(id);
    }

    queueMicrotask(() => {
      if (target.elementId) document.getElementById(target.elementId)?.focus();
    });
  }

  function onSubmit(formValues: FormValues) {
    setServerError(null);
    form.clearErrors();

    // Enter-semantik (bunden i CTO Q5): formen submittar bara på Spara-steget.
    // På steg 1-3 är Enter "gå vidare" — inte ett tyst sparförsök.
    if (step !== GUIDE_STEP_SAVE) {
      goToStep(step + 1);
      return;
    }

    const rawPayload = toRawPayload(formValues);
    // Klient-validering speglar server-actionen (server-validering är auktoritativ).
    const parsed = schema.safeParse({
      parsedResumeId: parsedId,
      name: formValues.name,
      content: rawPayload,
    });
    if (!parsed.success) {
      // ALLA issues ytas på sina fält — inte bara issues[0]. Den gamla formen gav
      // ett fel i taget i foten, så fem fel kostade fem submit-varv.
      let firstPath: string | null = null;
      for (const issue of parsed.error.issues) {
        const path = issue.path.join(".");
        firstPath ??= path;
        const formPath = guidePathToFormPath(path);
        if (formPath) {
          form.setError(formPath as FieldPath<FormValues>, {
            type: "validate",
            message: issue.message,
          });
        }
      }
      if (firstPath) {
        routeToError(firstPath);
        // Foten sammanfattar; fälten bär detaljerna (och fokus landar på det
        // första felande fältet, vars aria-describedby läser upp sitt eget fel).
        setServerError({ path: firstPath, message: tr("fixFields") });
      } else {
        setServerError({ path: null, message: tr("invalidData") });
      }
      return;
    }
    startTransition(async () => {
      // NEXT_REDIRECT (→ /cv) är en framgångssignal som får propagera. Bara ett
      // returnerat success:false hanteras som fel här.
      const result = await promoteParsedResumeFromGuideAction(
        parsedId,
        parsed.data.name,
        // Schemats utdata matchar ResumeContentDto:ns superset-form (languages +
        // sections); casten dokumenterar övergången från validerad ingångsform.
        parsed.data.content as ResumeContentDto,
      );
      if (!result.success) {
        setServerError({ path: null, message: result.error });
      }
    });
  }

  const summaryText = watch("summary") ?? "";
  const wordCount =
    summaryText.trim().length === 0 ? 0 : summaryText.trim().split(/\s+/).length;

  const stepLabels = [
    tr("steps.details"),
    tr("steps.experience"),
    tr("steps.skills"),
    tr("steps.save"),
  ];

  // next-intl kräver LITERALA nyckelsträngar; en helper med bara literaler löser
  // den dynamiska contact-fält-etiketten (CONTACT_FIELDS bär bara nyckeln).
  function contactLabel(key: ContactKey): string {
    switch (key) {
      case "fullName":
        return tr("details.fullNameLabel");
      case "email":
        return tr("details.emailLabel");
      case "phone":
        return tr("details.phoneLabel");
      case "location":
        return tr("details.locationLabel");
    }
  }

  return (
    // En riktig <form>: Enter submittar (steg 1-3 = gå vidare, steg 4 = spara).
    // Roten var en <div> och Spara en type="button" → Enter gjorde ingenting alls.
    //
    // `noValidate` med flit: Zod-schemat (samma som server-actionen kör) är ENDA
    // valideringsauktoriteten. Native constraint-validation skulle annars hinna
    // först på type="email"/"date"/required, blockera submit med webbläsarens egna
    // engelska bubblor (§10: svensk copy) och kortsluta våra per-fält-fel. Fälten
    // behåller required/aria-required — de är sanna påståenden om fältet och bär
    // SR-semantiken; det är auktoriteten, inte kravet, som flyttas till Zod.
    <form className="jp-guide" onSubmit={form.handleSubmit(onSubmit)} noValidate>
      {/* Header-rad: mono-källrad + Stäng (honesty bind 2 — aldrig "spara utkast"). */}
      <div className="jp-guide__head">
        <p className="jp-guide__source">
          {tr("sourceLine", { fileName: sourceFileName })}
        </p>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={requestClose}
          disabled={isPending}
        >
          {tr("close")}
        </Button>
      </div>

      {/* Klickbar stegrad (fri navigering — inget steg blockeras). */}
      <nav className="jp-guide__rail" aria-label={tr("railNavLabel")}>
        <ol className="jp-guide__raillist">
          {stepLabels.map((label, index) => {
            const active = index === step;
            // Markering (var jag står) och status (vad steget säger) är två skilda
            // axlar — den gamla indikatorn slog ihop dem i ett enda data-state.
            const status = stepState(index);
            const statusLabel = stepStatusLabel(index);
            return (
              <li key={index}>
                <button
                  type="button"
                  className="jp-guide__railitem"
                  data-state={active ? "active" : "idle"}
                  aria-current={active ? "step" : undefined}
                  onClick={() => goToStep(index)}
                  disabled={isPending}
                >
                  <span
                    className="jp-guide__railind"
                    data-status={status}
                    aria-hidden="true"
                  >
                    {status === "done" ? (
                      <Check size={14} strokeWidth={3} />
                    ) : status === "attention" ? (
                      <AlertTriangle size={14} strokeWidth={2.5} />
                    ) : (
                      <span>{index + 1}</span>
                    )}
                  </span>
                  <span className="jp-guide__raillabel">{label}</span>
                  {statusLabel && <span className="sr-only">{statusLabel}</span>}
                </button>
              </li>
            );
          })}
        </ol>
      </nav>

      <p className="jp-guide__position">
        {tr("stepPosition", { step: step + 1, total: STEP_COUNT })}
      </p>

      <div className="jp-guide__body">
        {/* ── Steg 1: Uppgifter ─────────────────────────────────────── */}
        {step === GUIDE_STEP_DETAILS && (
          <section aria-labelledby="guide-step-details">
            <h2
              id="guide-step-details"
              ref={titleRef}
              tabIndex={-1}
              className="jp-guide__steptitle"
            >
              {tr("steps.details")}
            </h2>
            <p className="jp-guide__intro">{tr("details.intro")}</p>

            <div className="jp-guide__fields">
              {CONTACT_FIELDS.map((field) => {
                const label = contactLabel(field.key);
                const found = isPresent(content.contact[field.key]);
                const confirmMode = found && !expandedContact.has(field.key);
                if (confirmMode) {
                  return (
                    <div key={field.key} className="jp-guide__confirm">
                      <span className="jp-guide__confirm-check" aria-hidden="true">
                        <Check size={16} strokeWidth={3} />
                      </span>
                      <span className="jp-guide__confirm-text">
                        <span className="jp-guide__confirm-label">{label}</span>
                        <span className="jp-guide__confirm-value">
                          {watch(`personalInfo.${field.key}`)}
                        </span>
                      </span>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        onClick={() => expandContact(field.key)}
                        aria-label={tr("changeNamed", { field: label })}
                        disabled={isPending}
                      >
                        {tr("change")}
                      </Button>
                    </div>
                  );
                }
                return (
                  <div key={field.key} className="jp-guide__field">
                    <div className="jp-guide__field-head">
                      <Label htmlFor={`guide-pi-${field.key}`}>
                        {label}
                        {!field.optional && (
                          <span aria-hidden="true" className="text-danger-600">
                            {" *"}
                          </span>
                        )}
                      </Label>
                      {!found && (
                        <span className="jp-guide__tags">
                          <StatusPill tone="neutral" dot={false}>
                            {tr("missingInFile")}
                          </StatusPill>
                          {field.optional && (
                            <StatusPill tone="neutral" dot={false}>
                              {tr("optional")}
                            </StatusPill>
                          )}
                        </span>
                      )}
                    </div>
                    <Input
                      id={`guide-pi-${field.key}`}
                      type={field.type}
                      {...register(`personalInfo.${field.key}`)}
                      required={!field.optional}
                      aria-required={!field.optional || undefined}
                      aria-invalid={
                        formState.errors.personalInfo?.[field.key]
                          ? true
                          : undefined
                      }
                      aria-describedby={describedBy(
                        formState.errors.personalInfo?.[field.key] &&
                          `guide-pi-${field.key}-error`,
                      )}
                      maxLength={field.key === "phone" ? 50 : 200}
                      disabled={isPending}
                    />
                    <FieldMessage
                      id={`guide-pi-${field.key}-error`}
                      message={formState.errors.personalInfo?.[field.key]?.message}
                    />
                  </div>
                );
              })}

              <div className="jp-guide__field">
                <div className="jp-guide__field-head">
                  <Label htmlFor="guide-summary">
                    {tr("details.profileLabel")}
                  </Label>
                  {isPresent(content.profile) ? (
                    <span className="jp-guide__found">
                      <Check size={14} strokeWidth={3} aria-hidden="true" />
                      {tr("foundInFile")}
                    </span>
                  ) : (
                    <StatusPill tone="neutral" dot={false}>
                      {tr("missingInFile")}
                    </StatusPill>
                  )}
                </div>
                <p id="guide-summary-hint" className="jp-guide__hint">
                  {tr("details.profileHint")}
                </p>
                {isDegraded("Profile") && (
                  <p className="jp-guide__degraded">{tr("degradedNote")}</p>
                )}
                <Textarea
                  id="guide-summary"
                  {...register("summary")}
                  aria-invalid={formState.errors.summary ? true : undefined}
                  aria-describedby={describedBy(
                    "guide-summary-hint",
                    "guide-summary-count",
                    formState.errors.summary && "guide-summary-error",
                  )}
                  rows={4}
                  maxLength={2000}
                  disabled={isPending}
                />
                <FieldMessage
                  id="guide-summary-error"
                  message={formState.errors.summary?.message}
                />
                <p
                  id="guide-summary-count"
                  className="jp-guide__wordcount"
                  role="status"
                  aria-live="polite"
                >
                  {tr("wordCount", { count: wordCount })}
                </p>
              </div>
            </div>
          </section>
        )}

        {/* ── Steg 2: Erfarenhet och utbildning ─────────────────────── */}
        {step === GUIDE_STEP_EXPERIENCE && (
          <section aria-labelledby="guide-step-experience">
            <h2
              id="guide-step-experience"
              ref={titleRef}
              tabIndex={-1}
              className="jp-guide__steptitle"
            >
              {tr("steps.experience")}
            </h2>
            <p className="jp-guide__intro">{tr("experience.intro")}</p>

            {/* Erfarenhet */}
            <div className="jp-guide__section">
              <div className="jp-guide__section-head">
                <h3 className="jp-guide__section-title">
                  {tr("experience.experienceHeading")}
                </h3>
                <FoundProvenance count={content.experiences.length} />
              </div>
              {isDegraded("Experience") && (
                <p className="jp-guide__degraded">{tr("degradedNote")}</p>
              )}
              {experiences.fields.length === 0 && (
                <p className="jp-guide__empty">{tr("experience.noExperience")}</p>
              )}
              <div className="jp-guide__cards">
                {experiences.fields.map((field, index) => (
                  <ExperienceCard
                    key={field.id}
                    form={form}
                    index={index}
                    expanded={!collapsedEntries.has(field.id)}
                    onExpand={() => expandEntry(field.id)}
                    onRemove={() => experiences.remove(index)}
                    disabled={isPending}
                  />
                ))}
              </div>
              <div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => experiences.append(emptyExperience())}
                  disabled={isPending}
                >
                  <Plus size={14} aria-hidden="true" /> {tr("experience.addExperience")}
                </Button>
              </div>
            </div>

            {/* Utbildning */}
            <div className="jp-guide__section">
              <div className="jp-guide__section-head">
                <h3 className="jp-guide__section-title">
                  {tr("experience.educationHeading")}
                </h3>
                <FoundProvenance count={content.educations.length} />
              </div>
              {isDegraded("Education") && (
                <p className="jp-guide__degraded">{tr("degradedNote")}</p>
              )}
              {educations.fields.length === 0 && (
                <p className="jp-guide__empty">{tr("experience.noEducation")}</p>
              )}
              <div className="jp-guide__cards">
                {educations.fields.map((field, index) => (
                  <EducationCard
                    key={field.id}
                    form={form}
                    index={index}
                    expanded={!collapsedEntries.has(field.id)}
                    onExpand={() => expandEntry(field.id)}
                    onRemove={() => educations.remove(index)}
                    disabled={isPending}
                  />
                ))}
              </div>
              <div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => educations.append(emptyEducation())}
                  disabled={isPending}
                >
                  <Plus size={14} aria-hidden="true" /> {tr("experience.addEducation")}
                </Button>
              </div>
            </div>

            {/* Egna sektioner (generisk panel, CTO Q7(a) — fri rubrik, inga förslag).
                Proveniensen saknades här medan Erfarenhet/Utbildning bar sin: sedan
                #849 prefylls PROJEKT/REFERENSER ur filen, och då måste ytan säga att
                de KOM ur filen — annars ser användarens egna sektioner ut som något
                guiden hittade på. */}
            <div className="jp-guide__section">
              <div className="jp-guide__section-head">
                <h3 className="jp-guide__section-title">
                  {tr("experience.sectionsHeading")}
                </h3>
                <FoundProvenance count={content.sections.length} optional />
              </div>
              <p className="jp-guide__intro">{tr("experience.sectionsIntro")}</p>
              {sections.fields.length === 0 && (
                <p className="jp-guide__empty">{tr("experience.noSections")}</p>
              )}
              <div className="jp-guide__cards">
                {sections.fields.map((field, index) => (
                  <SectionCard
                    key={field.id}
                    form={form}
                    index={index}
                    onRemove={() => sections.remove(index)}
                    disabled={isPending}
                  />
                ))}
              </div>
              <div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() =>
                    sections.append({ heading: "", entries: [{ title: "", body: "" }] })
                  }
                  disabled={isPending}
                >
                  <Plus size={14} aria-hidden="true" /> {tr("experience.addSection")}
                </Button>
              </div>
            </div>
          </section>
        )}

        {/* ── Steg 3: Kompetenser ───────────────────────────────────── */}
        {step === GUIDE_STEP_SKILLS && (
          <section aria-labelledby="guide-step-skills">
            <h2
              id="guide-step-skills"
              ref={titleRef}
              tabIndex={-1}
              className="jp-guide__steptitle"
            >
              {tr("steps.skills")}
            </h2>
            <p className="jp-guide__intro">{tr("skills.intro")}</p>

            <div className="jp-guide__parallel">
              <div className="jp-guide__card">
                <div className="jp-guide__section-head">
                  <h3 className="jp-guide__section-title">
                    {tr("skills.skillsHeading")}
                  </h3>
                  <FoundProvenance count={content.skills.length} />
                </div>
                {isDegraded("Skills") && (
                  <p className="jp-guide__degraded">{tr("degradedNote")}</p>
                )}
                <ChipEditor
                  inputId="guide-skills-add"
                  names={watch("skills").map((s) => s.name)}
                  onAdd={(name) => skills.append({ name })}
                  onRemove={(index) => skills.remove(index)}
                  addLabel={tr("skills.skillsAddLabel")}
                  addButtonLabel={tr("skills.add")}
                  emptyLabel={tr("skills.skillsEmpty")}
                  removeLabel={(name) => tr("skills.removeSkill", { name })}
                  disabled={isPending}
                />
              </div>

              <div className="jp-guide__card">
                <div className="jp-guide__section-head">
                  <h3 className="jp-guide__section-title">
                    {tr("skills.languagesHeading")}
                  </h3>
                  <FoundProvenance count={content.languages.length} />
                </div>
                {isDegraded("Languages") && (
                  <p className="jp-guide__degraded">{tr("degradedNote")}</p>
                )}
                <ChipEditor
                  inputId="guide-languages-add"
                  names={watch("languages").map((l) => l.name)}
                  onAdd={(name) => languages.append({ name })}
                  onRemove={(index) => languages.remove(index)}
                  addLabel={tr("skills.languagesAddLabel")}
                  addButtonLabel={tr("skills.add")}
                  emptyLabel={tr("skills.languagesEmpty")}
                  removeLabel={(name) => tr("skills.removeLanguage", { name })}
                  disabled={isPending}
                />
              </div>
            </div>
          </section>
        )}

        {/* ── Steg 4: Spara ─────────────────────────────────────────── */}
        {step === GUIDE_STEP_SAVE && (
          <section aria-labelledby="guide-step-save">
            <h2
              id="guide-step-save"
              ref={titleRef}
              tabIndex={-1}
              className="jp-guide__steptitle"
            >
              {tr("steps.save")}
            </h2>
            <p className="jp-guide__intro">{tr("save.intro")}</p>

            {/* Samma predikat som railen (CTO-bind Q3-A) — de två ytorna kan inte
                säga emot varandra, för de läser samma tre tillstånd. */}
            <ul className="jp-guide__summary">
              {[GUIDE_STEP_DETAILS, GUIDE_STEP_EXPERIENCE, GUIDE_STEP_SKILLS].map(
                (stepIndex) => {
                  const status = stepState(stepIndex);
                  const blockers = blockersForStep(stepIndex);
                  const remaining = remainingForStep(stepIndex);
                  const partLabel = stepLabels[stepIndex] ?? "";
                  return (
                    <li key={stepIndex} className="jp-guide__summary-row">
                      <span className="jp-guide__summary-part">
                        <span
                          className="jp-guide__summary-ind"
                          data-status={status}
                          aria-hidden="true"
                        >
                          {status === "done" ? (
                            <Check size={14} strokeWidth={3} />
                          ) : status === "attention" ? (
                            <AlertTriangle size={14} strokeWidth={2.5} />
                          ) : (
                            remaining
                          )}
                        </span>
                        {partLabel}
                      </span>
                      {status === "done" ? (
                        <span className="jp-guide__summary-status">
                          {tr("save.summaryDone")}
                        </span>
                      ) : (
                        <span className="jp-guide__summary-status">
                          {status === "attention"
                            ? tr("save.summaryBlocked", { count: blockers })
                            : tr("save.summaryRemaining", { count: remaining })}
                          <Button
                            type="button"
                            variant="link"
                            size="sm"
                            onClick={() => goToStep(stepIndex)}
                            disabled={isPending}
                          >
                            {tr("save.summaryJump", { step: partLabel })}
                          </Button>
                        </span>
                      )}
                    </li>
                  );
                },
              )}
            </ul>

            {/* Obligatoriskt copy-bind (CTO Q3-A): "kvar" får ALDRIG läsas som
                "krävs". Uppgifterna som återstår är frivilliga — säg det rakt ut,
                annars är den ärliga modellen ändå oärlig i sin ton. */}
            {hasRemainingTasks && !hasBlockers && (
              <p className="jp-guide__note">{tr("save.remainingOptional")}</p>
            )}

            <div className="jp-guide__field">
              <Label htmlFor="guide-cv-name">
                {tr("save.nameLabel")}
                <span aria-hidden="true" className="text-danger-600">
                  {" *"}
                </span>
              </Label>
              <p id="guide-cv-name-hint" className="jp-guide__hint">
                {tr("save.nameHint")}
              </p>
              <Input
                id="guide-cv-name"
                {...register("name")}
                aria-invalid={formState.errors.name ? true : undefined}
                aria-describedby={describedBy(
                  "guide-cv-name-hint",
                  formState.errors.name && "guide-cv-name-error",
                )}
                maxLength={200}
                required
                aria-required={true}
                disabled={isPending}
              />
              <FieldMessage
                id="guide-cv-name-error"
                message={formState.errors.name?.message}
              />
            </div>

            <p className="jp-guide__note">{tr("save.sourceUntouched")}</p>
          </section>
        )}
      </div>

      {/* Footer: navigering + spara. */}
      <div className="jp-guide__foot">
        {step > GUIDE_STEP_DETAILS && (
          <Button
            type="button"
            variant="ghost"
            onClick={() => goToStep(step - 1)}
            disabled={isPending}
          >
            {tr("back")}
          </Button>
        )}
        <span className="jp-guide__foot-spacer" />
        {serverError && (
          <p id={ERROR_ID} role="alert" className="jp-guide__error">
            {serverError.message}
          </p>
        )}
        {step < GUIDE_STEP_SAVE ? (
          // type="submit" även här — inte bara för musklicket, utan för att en form
          // UTAN submit-knapp inte har någon implicit submission alls: Enter i ett
          // fält hade fortsatt vara dött på steg 1-3 (HTML-specens default button).
          // onSubmit avgör innebörden: gå vidare på steg 1-3, spara på steg 4.
          <Button type="submit" disabled={isPending}>
            {tr("next")}
          </Button>
        ) : (
          // Spara avaktiveras ALDRIG av kvarvarande uppgifter (CTO Q3-A): de är
          // frivilliga. Bara ett äkta blockerande fel stoppar sparandet, och då
          // förklaras det på fältet — förklara, blockera inte.
          <Button type="submit" disabled={isPending}>
            {isPending ? tr("save.pending") : tr("save.cta")}
          </Button>
        )}
      </div>

      {/* Stäng-bekräftelse (honesty bind 2): visas bara när formen är ändrad. */}
      <Dialog open={confirmClose} onOpenChange={setConfirmClose}>
        <DialogContent>
          <DialogTitle>{tr("closeConfirmTitle")}</DialogTitle>
          <DialogDescription>{tr("closeConfirmBody")}</DialogDescription>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setConfirmClose(false)}
            >
              {tr("closeConfirmCancel")}
            </Button>
            {/* Discard-handlingen får ALDRIG vara den gröna primaryn (design-
                reviewer Minor PR-8.3): destructive-ton, paritet med
                DiscardDraftButtons bekräfta-dialog. */}
            <Button
              type="button"
              variant="destructive"
              onClick={() => router.push(CLOSE_HREF)}
            >
              {tr("closeConfirmConfirm")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </form>
  );
}

/**
 * Felmeddelandet för ETT fält. Inte `role="alert"`: vid submit sätts flera fel
 * samtidigt, och lika många samtidiga alerts blir en uppläsnings-storm som döljer
 * just det fält fokus landar på. Meddelandet kopplas i stället till sitt fält via
 * `aria-describedby` (+ `aria-invalid`), så skärmläsaren läser felet när fokus
 * flyttas dit — vilket `routeToError` gör med det första felet. Foten bär den
 * aggregerade signalen (`role="alert"`).
 */
function FieldMessage({ id, message }: { id: string; message?: string }) {
  if (!message) return null;
  return (
    <p id={id} className="jp-guide__fielderror">
      {message}
    </p>
  );
}

/**
 * `aria-describedby` får peka på flera id:n. Ett fält kan ha BÅDE hjälptext och
 * fel — hjälptexten förklarar fältet, felet förklarar varför det inte gick igenom,
 * och båda behövs. Tomma led filtreras bort; allt tomt → `undefined` (aldrig ett
 * tomt describedby som pekar i luften).
 */
function describedBy(...ids: Array<string | false | undefined>): string | undefined {
  const present = ids.filter((id): id is string => Boolean(id));
  return present.length > 0 ? present.join(" ") : undefined;
}

/**
 * Found-count-proveniens per sektion: "{n} hittade" eller "Saknades i filen".
 *
 * `optional` — sektioner som INTE är en uppgift att stänga (fria sektioner:
 * Projekt, Referenser, Certifikat). För dem är noll träffar inget saknat: ett CV
 * utan PROJEKT saknar ingenting, och "Saknades i filen" hade påstått en lucka som
 * inte finns. De ytar därför proveniensen bara när parsern faktiskt hittade något;
 * tomt läge bärs av panelens egen empty-text.
 */
function FoundProvenance({
  count,
  optional = false,
}: {
  count: number;
  optional?: boolean;
}) {
  const tr = useTranslations("resumes.guide");
  if (count === 0) {
    if (optional) return null;
    return (
      <StatusPill tone="neutral" dot={false}>
        {tr("missingInFile")}
      </StatusPill>
    );
  }
  return (
    <span className="jp-guide__found">
      <Check size={14} strokeWidth={3} aria-hidden="true" />
      {tr("foundCount", { count })}
    </span>
  );
}

function ExperienceCard({
  form,
  index,
  expanded,
  onExpand,
  onRemove,
  disabled,
}: {
  form: UseFormReturn<FormValues>;
  index: number;
  expanded: boolean;
  onExpand: () => void;
  onRemove: () => void;
  disabled: boolean;
}) {
  const tr = useTranslations("resumes.guide");
  const { register, control, watch, setValue, formState } = form;
  const role = watch(`experiences.${index}.role`);
  const company = watch(`experiences.${index}.company`);
  const startDate = watch(`experiences.${index}.startDate`);
  const ongoing = watch(`experiences.${index}.ongoing`);
  const periodHint = watch(`experiences.${index}.periodHint`);
  const dateHintId = periodHint ? `guide-exp-${index}-period` : undefined;
  const errors = formState.errors.experiences?.[index];

  if (!expanded) {
    const summary = [role, company].filter((v) => v.trim().length > 0).join(" · ");
    return (
      <div className="jp-guide__collapsed">
        <span className="jp-guide__collapsed-text">
          <span className="jp-guide__collapsed-title">
            {summary || tr("experience.experienceLegend", { index: index + 1 })}
          </span>
          {periodHint && (
            <span className="jp-guide__collapsed-meta">
              {tr("experience.periodHint", { period: periodHint })}
            </span>
          )}
        </span>
        {/* #815: this used to read "Datum saknas" on EVERY entry. The parser deliberately
            never guesses dates (DQ3-3a), so the structured dates always start empty — while
            the period the CV DOES state is rendered right above as periodHint. The pill was
            therefore claiming the dates were missing one line under the dates. What is
            actually missing is the user's CONFIRMATION, which is this guide's whole doctrine
            (confirm, never fill). "Datum saknas" now means what it says: the CV states no
            period at all. */}
        {startDate.trim().length === 0 &&
          (periodHint.trim().length > 0 ? (
            <StatusPill tone="neutral" dot={false}>
              {tr("experience.dateConfirm")}
            </StatusPill>
          ) : (
            <StatusPill tone="warning" dot={false}>
              {tr("experience.dateMissing")}
            </StatusPill>
          ))}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onExpand}
          aria-label={tr("experience.editExperience", { index: index + 1 })}
          disabled={disabled}
        >
          {tr("change")}
        </Button>
      </div>
    );
  }

  return (
    <fieldset className="jp-guide__entry">
      <legend className="sr-only">
        {tr("experience.experienceLegend", { index: index + 1 })}
      </legend>
      <div className="jp-guide__grid">
        <div className="jp-guide__field">
          <Label htmlFor={`guide-exp-${index}-company`}>
            {tr("experience.companyLabel")}
            <span aria-hidden="true" className="text-danger-600">{" *"}</span>
          </Label>
          <Input
            id={`guide-exp-${index}-company`}
            {...register(`experiences.${index}.company`)}
            maxLength={200}
            required
            aria-required={true}
            aria-invalid={errors?.company ? true : undefined}
            aria-describedby={describedBy(
              errors?.company && `guide-exp-${index}-company-error`,
            )}
            disabled={disabled}
          />
          <FieldMessage
            id={`guide-exp-${index}-company-error`}
            message={errors?.company?.message}
          />
        </div>
        <div className="jp-guide__field">
          <Label htmlFor={`guide-exp-${index}-role`}>
            {tr("experience.roleLabel")}
            <span aria-hidden="true" className="text-danger-600">{" *"}</span>
          </Label>
          <Input
            id={`guide-exp-${index}-role`}
            {...register(`experiences.${index}.role`)}
            maxLength={200}
            required
            aria-required={true}
            aria-invalid={errors?.role ? true : undefined}
            aria-describedby={describedBy(
              errors?.role && `guide-exp-${index}-role-error`,
            )}
            disabled={disabled}
          />
          <FieldMessage
            id={`guide-exp-${index}-role-error`}
            message={errors?.role?.message}
          />
        </div>
      </div>

      <div
        className="jp-guide__field"
        role="group"
        aria-label={tr("experience.periodGroupLabel")}
        aria-describedby={dateHintId}
      >
        <div className="jp-guide__grid">
          <div className="jp-guide__field">
            <Label htmlFor={`guide-exp-${index}-startDate`}>
              {tr("experience.startDateLabel")}
              <span aria-hidden="true" className="text-danger-600">{" *"}</span>
            </Label>
            <Input
              id={`guide-exp-${index}-startDate`}
              type="date"
              {...register(`experiences.${index}.startDate`)}
              required
              aria-required={true}
              aria-invalid={errors?.startDate ? true : undefined}
              aria-describedby={describedBy(
                errors?.startDate && `guide-exp-${index}-startDate-error`,
              )}
              disabled={disabled}
            />
            <FieldMessage
              id={`guide-exp-${index}-startDate-error`}
              message={errors?.startDate?.message}
            />
          </div>
          {!ongoing && (
            <div className="jp-guide__field">
              <Label htmlFor={`guide-exp-${index}-endDate`}>
                {tr("experience.endDateLabel")}
              </Label>
              <Input
                id={`guide-exp-${index}-endDate`}
                type="date"
                {...register(`experiences.${index}.endDate`)}
                aria-invalid={errors?.endDate ? true : undefined}
                aria-describedby={describedBy(
                  errors?.endDate && `guide-exp-${index}-endDate-error`,
                )}
                disabled={disabled}
              />
              <FieldMessage
                id={`guide-exp-${index}-endDate-error`}
                message={errors?.endDate?.message}
              />
            </div>
          )}
        </div>
        {periodHint && (
          <p id={dateHintId} className="jp-guide__hint">
            {tr("experience.periodHint", { period: periodHint })}
          </p>
        )}
        <Controller
          control={control}
          name={`experiences.${index}.ongoing`}
          render={({ field }) => (
            <ToggleRow
              label={tr("experience.ongoingLabel")}
              description={tr("experience.ongoingHint")}
              checked={field.value}
              onChange={(next) => {
                field.onChange(next);
                if (next) setValue(`experiences.${index}.endDate`, "");
              }}
              disabled={disabled}
            />
          )}
        />
      </div>

      <div className="jp-guide__field">
        <Label htmlFor={`guide-exp-${index}-description`}>
          {tr("experience.descriptionLabel")}
        </Label>
        <Textarea
          id={`guide-exp-${index}-description`}
          {...register(`experiences.${index}.description`)}
          aria-invalid={errors?.description ? true : undefined}
          aria-describedby={describedBy(
            errors?.description && `guide-exp-${index}-description-error`,
          )}
          rows={3}
          maxLength={2000}
          disabled={disabled}
        />
        <FieldMessage
          id={`guide-exp-${index}-description-error`}
          message={errors?.description?.message}
        />
      </div>

      <div>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRemove}
          aria-label={tr("experience.removeExperience", { index: index + 1 })}
          disabled={disabled}
        >
          {tr("remove")}
        </Button>
      </div>
    </fieldset>
  );
}

function EducationCard({
  form,
  index,
  expanded,
  onExpand,
  onRemove,
  disabled,
}: {
  form: UseFormReturn<FormValues>;
  index: number;
  expanded: boolean;
  onExpand: () => void;
  onRemove: () => void;
  disabled: boolean;
}) {
  const tr = useTranslations("resumes.guide");
  const { register, control, watch, setValue, formState } = form;
  const degree = watch(`educations.${index}.degree`);
  const institution = watch(`educations.${index}.institution`);
  const startDate = watch(`educations.${index}.startDate`);
  const ongoing = watch(`educations.${index}.ongoing`);
  const periodHint = watch(`educations.${index}.periodHint`);
  const dateHintId = periodHint ? `guide-edu-${index}-period` : undefined;
  const errors = formState.errors.educations?.[index];

  if (!expanded) {
    const summary = [degree, institution]
      .filter((v) => v.trim().length > 0)
      .join(" · ");
    return (
      <div className="jp-guide__collapsed">
        <span className="jp-guide__collapsed-text">
          <span className="jp-guide__collapsed-title">
            {summary || tr("experience.educationLegend", { index: index + 1 })}
          </span>
          {periodHint && (
            <span className="jp-guide__collapsed-meta">
              {tr("experience.periodHint", { period: periodHint })}
            </span>
          )}
        </span>
        {/* #815: this used to read "Datum saknas" on EVERY entry. The parser deliberately
            never guesses dates (DQ3-3a), so the structured dates always start empty — while
            the period the CV DOES state is rendered right above as periodHint. The pill was
            therefore claiming the dates were missing one line under the dates. What is
            actually missing is the user's CONFIRMATION, which is this guide's whole doctrine
            (confirm, never fill). "Datum saknas" now means what it says: the CV states no
            period at all. */}
        {startDate.trim().length === 0 &&
          (periodHint.trim().length > 0 ? (
            <StatusPill tone="neutral" dot={false}>
              {tr("experience.dateConfirm")}
            </StatusPill>
          ) : (
            <StatusPill tone="warning" dot={false}>
              {tr("experience.dateMissing")}
            </StatusPill>
          ))}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onExpand}
          aria-label={tr("experience.editEducation", { index: index + 1 })}
          disabled={disabled}
        >
          {tr("change")}
        </Button>
      </div>
    );
  }

  return (
    <fieldset className="jp-guide__entry">
      <legend className="sr-only">
        {tr("experience.educationLegend", { index: index + 1 })}
      </legend>
      <div className="jp-guide__grid">
        <div className="jp-guide__field">
          <Label htmlFor={`guide-edu-${index}-institution`}>
            {tr("experience.institutionLabel")}
            <span aria-hidden="true" className="text-danger-600">{" *"}</span>
          </Label>
          <Input
            id={`guide-edu-${index}-institution`}
            {...register(`educations.${index}.institution`)}
            maxLength={200}
            required
            aria-required={true}
            aria-invalid={errors?.institution ? true : undefined}
            aria-describedby={describedBy(
              errors?.institution && `guide-edu-${index}-institution-error`,
            )}
            disabled={disabled}
          />
          <FieldMessage
            id={`guide-edu-${index}-institution-error`}
            message={errors?.institution?.message}
          />
        </div>
        <div className="jp-guide__field">
          <Label htmlFor={`guide-edu-${index}-degree`}>
            {tr("experience.degreeLabel")}
            <span aria-hidden="true" className="text-danger-600">{" *"}</span>
          </Label>
          <Input
            id={`guide-edu-${index}-degree`}
            {...register(`educations.${index}.degree`)}
            maxLength={200}
            required
            aria-required={true}
            aria-invalid={errors?.degree ? true : undefined}
            aria-describedby={describedBy(
              errors?.degree && `guide-edu-${index}-degree-error`,
            )}
            disabled={disabled}
          />
          <FieldMessage
            id={`guide-edu-${index}-degree-error`}
            message={errors?.degree?.message}
          />
        </div>
      </div>

      <div
        className="jp-guide__field"
        role="group"
        aria-label={tr("experience.periodGroupLabel")}
        aria-describedby={dateHintId}
      >
        <div className="jp-guide__grid">
          <div className="jp-guide__field">
            <Label htmlFor={`guide-edu-${index}-startDate`}>
              {tr("experience.startDateLabel")}
              <span aria-hidden="true" className="text-danger-600">{" *"}</span>
            </Label>
            <Input
              id={`guide-edu-${index}-startDate`}
              type="date"
              {...register(`educations.${index}.startDate`)}
              required
              aria-required={true}
              aria-invalid={errors?.startDate ? true : undefined}
              aria-describedby={describedBy(
                errors?.startDate && `guide-edu-${index}-startDate-error`,
              )}
              disabled={disabled}
            />
            <FieldMessage
              id={`guide-edu-${index}-startDate-error`}
              message={errors?.startDate?.message}
            />
          </div>
          {!ongoing && (
            <div className="jp-guide__field">
              <Label htmlFor={`guide-edu-${index}-endDate`}>
                {tr("experience.endDateLabel")}
              </Label>
              <Input
                id={`guide-edu-${index}-endDate`}
                type="date"
                {...register(`educations.${index}.endDate`)}
                aria-invalid={errors?.endDate ? true : undefined}
                aria-describedby={describedBy(
                  errors?.endDate && `guide-edu-${index}-endDate-error`,
                )}
                disabled={disabled}
              />
              <FieldMessage
                id={`guide-edu-${index}-endDate-error`}
                message={errors?.endDate?.message}
              />
            </div>
          )}
        </div>
        {periodHint && (
          <p id={dateHintId} className="jp-guide__hint">
            {tr("experience.periodHint", { period: periodHint })}
          </p>
        )}
        <Controller
          control={control}
          name={`educations.${index}.ongoing`}
          render={({ field }) => (
            <ToggleRow
              label={tr("experience.ongoingLabel")}
              description={tr("experience.ongoingHint")}
              checked={field.value}
              onChange={(next) => {
                field.onChange(next);
                if (next) setValue(`educations.${index}.endDate`, "");
              }}
              disabled={disabled}
            />
          )}
        />
      </div>

      <div>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRemove}
          aria-label={tr("experience.removeEducation", { index: index + 1 })}
          disabled={disabled}
        >
          {tr("remove")}
        </Button>
      </div>
    </fieldset>
  );
}

function SectionCard({
  form,
  index,
  onRemove,
  disabled,
}: {
  form: UseFormReturn<FormValues>;
  index: number;
  onRemove: () => void;
  disabled: boolean;
}) {
  const tr = useTranslations("resumes.guide");
  const { register, control, formState } = form;
  const entries = useFieldArray({ control, name: `sections.${index}.entries` });
  const errors = formState.errors.sections?.[index];

  return (
    <fieldset className="jp-guide__entry">
      <legend className="sr-only">
        {tr("experience.sectionLegend", { index: index + 1 })}
      </legend>
      <div className="jp-guide__field">
        <Label htmlFor={`guide-section-${index}-heading`}>
          {tr("experience.sectionHeadingLabel")}
          <span aria-hidden="true" className="text-danger-600">{" *"}</span>
        </Label>
        <p id={`guide-section-${index}-heading-hint`} className="jp-guide__hint">
          {tr("experience.sectionHeadingHint")}
        </p>
        <Input
          id={`guide-section-${index}-heading`}
          {...register(`sections.${index}.heading`)}
          aria-invalid={errors?.heading ? true : undefined}
          aria-describedby={describedBy(
            `guide-section-${index}-heading-hint`,
            errors?.heading && `guide-section-${index}-heading-error`,
          )}
          maxLength={200}
          required
          aria-required={true}
          disabled={disabled}
        />
        <FieldMessage
          id={`guide-section-${index}-heading-error`}
          message={errors?.heading?.message}
        />
      </div>

      {entries.fields.length === 0 && (
        <p className="jp-guide__empty">{tr("experience.noEntries")}</p>
      )}
      <div className="jp-guide__cards">
        {entries.fields.map((entryField, entryIndex) => (
          <div key={entryField.id} className="jp-guide__subentry">
            {/* Regeln bor på POST-nivå, inte på fältet: ingetdera fältet är obligatoriskt
                för sig — minst ett av dem måste fyllas i (#815, Resume.SectionEntryEmpty). */}
            <p className="jp-guide__hint">{tr("experience.entryHint")}</p>
            <div className="jp-guide__field">
              {/* #815: titeln är INTE obligatorisk. En post kan bära bara text
                  ("Referenser / Lämnas på begäran."), och parsern hittar aldrig på en
                  rubrik (ADR 0071). Stod asterisken och `required` kvar hade vi flyttat
                  rubrik-uppfinnandet från motorn till användaren — precis det model-
                  ändringen skulle ta bort. Regeln (titel ELLER text) hör hemma på
                  post-nivå, och zod-refinen ytar den där. */}
              <Label htmlFor={`guide-section-${index}-entry-${entryIndex}-title`}>
                {tr("experience.entryTitleLabel")}
              </Label>
              <Input
                id={`guide-section-${index}-entry-${entryIndex}-title`}
                {...register(`sections.${index}.entries.${entryIndex}.title`)}
                aria-invalid={
                  errors?.entries?.[entryIndex]?.title ? true : undefined
                }
                aria-describedby={describedBy(
                  errors?.entries?.[entryIndex]?.title &&
                    `guide-section-${index}-entry-${entryIndex}-title-error`,
                )}
                maxLength={200}
                disabled={disabled}
              />
              {/* Post-regeln (titel ELLER text) ytas av zod-refinen på `title` —
                  den enda platsen den KAN ytas utan att ljuga om vilket fält som
                  är fel, för det är kombinationen som saknas, inte fältet. */}
              <FieldMessage
                id={`guide-section-${index}-entry-${entryIndex}-title-error`}
                message={errors?.entries?.[entryIndex]?.title?.message}
              />
            </div>
            <div className="jp-guide__field">
              <Label htmlFor={`guide-section-${index}-entry-${entryIndex}-body`}>
                {tr("experience.entryBodyLabel")}
              </Label>
              <p
                id={`guide-section-${index}-entry-${entryIndex}-body-hint`}
                className="jp-guide__hint"
              >
                {tr("experience.entryBodyHint")}
              </p>
              <Textarea
                id={`guide-section-${index}-entry-${entryIndex}-body`}
                {...register(`sections.${index}.entries.${entryIndex}.body`)}
                aria-invalid={
                  errors?.entries?.[entryIndex]?.body ? true : undefined
                }
                aria-describedby={describedBy(
                  `guide-section-${index}-entry-${entryIndex}-body-hint`,
                  errors?.entries?.[entryIndex]?.body &&
                    `guide-section-${index}-entry-${entryIndex}-body-error`,
                )}
                rows={3}
                disabled={disabled}
              />
              <FieldMessage
                id={`guide-section-${index}-entry-${entryIndex}-body-error`}
                message={errors?.entries?.[entryIndex]?.body?.message}
              />
            </div>
            <div>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => entries.remove(entryIndex)}
                aria-label={tr("experience.removeEntry", { index: entryIndex + 1 })}
                disabled={disabled}
              >
                {tr("remove")}
              </Button>
            </div>
          </div>
        ))}
      </div>

      <div className="jp-guide__section-actions">
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => entries.append({ title: "", body: "" })}
          disabled={disabled}
        >
          <Plus size={14} aria-hidden="true" /> {tr("experience.addEntry")}
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRemove}
          aria-label={tr("experience.removeSection", { index: index + 1 })}
          disabled={disabled}
        >
          {tr("remove")}
        </Button>
      </div>
    </fieldset>
  );
}

function ChipEditor({
  inputId,
  names,
  onAdd,
  onRemove,
  addLabel,
  addButtonLabel,
  emptyLabel,
  removeLabel,
  disabled,
}: {
  inputId: string;
  names: string[];
  onAdd: (name: string) => void;
  onRemove: (index: number) => void;
  addLabel: string;
  addButtonLabel: string;
  emptyLabel: string;
  removeLabel: (name: string) => string;
  disabled: boolean;
}) {
  const [draft, setDraft] = useState("");

  function commit() {
    const value = draft.trim();
    if (value.length === 0) return;
    onAdd(value);
    setDraft("");
  }

  return (
    <div className="jp-guide__chipeditor">
      {names.length === 0 ? (
        <p className="jp-guide__empty">{emptyLabel}</p>
      ) : (
        <ul className="jp-chiplist">
          {names.map((name, index) => (
            <li key={`${name}-${index}`}>
              <span className="jp-chip jp-chip--removable">
                <span className="jp-chip__label">{name}</span>
                <button
                  type="button"
                  className="jp-chip__remove"
                  onClick={() => onRemove(index)}
                  aria-label={removeLabel(name)}
                  disabled={disabled}
                >
                  <X size={14} aria-hidden="true" />
                </button>
              </span>
            </li>
          ))}
        </ul>
      )}
      <div className="jp-guide__chipadd">
        <Label htmlFor={inputId} className="sr-only">
          {addLabel}
        </Label>
        <Input
          id={inputId}
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") {
              event.preventDefault();
              commit();
            }
          }}
          maxLength={100}
          disabled={disabled}
        />
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={commit}
          disabled={disabled}
        >
          <Plus size={14} aria-hidden="true" /> {addButtonLabel}
        </Button>
      </div>
    </div>
  );
}

function emptyExperience(): ExperienceFormValue {
  return {
    company: "",
    role: "",
    startDate: "",
    endDate: "",
    ongoing: false,
    description: "",
    periodHint: "",
  };
}

function emptyEducation(): EducationFormValue {
  return {
    institution: "",
    degree: "",
    startDate: "",
    endDate: "",
    ongoing: false,
    periodHint: "",
  };
}
