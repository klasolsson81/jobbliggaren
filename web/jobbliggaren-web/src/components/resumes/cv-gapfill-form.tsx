"use client";

// Client Component: gap-fill-formen är interaktiv (RHF + useFieldArray-state,
// onSubmit-handler, useTransition, programmatisk focus-flytt vid valideringsfel).
// CV-PII tas emot som props från RSC:n (server-only läsning) men redigeras här.

import { useEffect, useState, useTransition } from "react";
import { useForm, useFieldArray, Controller } from "react-hook-form";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { promoteParsedResumeSchema } from "@/lib/actions/resume-schemas";
import { promoteParsedResumeAction } from "@/lib/actions/resumes";
import { pathToElementId } from "@/lib/forms/resume-path-routing";
import type { ParsedContentDto } from "@/lib/dto/parsed-resume";
import type { ResumeContentDto } from "@/lib/types/resumes";

interface CvGapFillFormProps {
  parsedId: string;
  sourceFileName: string;
  content: ParsedContentDto;
}

type FormValues = {
  name: string;
  personalInfo: {
    fullName: string;
    email: string;
    phone: string;
    location: string;
  };
  experiences: Array<{
    company: string;
    role: string;
    startDate: string;
    endDate: string;
    description: string;
    // Display-only tolkad period från parsern. Ingår ALDRIG i payloaden.
    periodHint: string;
  }>;
  educations: Array<{
    institution: string;
    degree: string;
    startDate: string;
    endDate: string;
    // Display-only tolkad period från parsern. Ingår ALDRIG i payloaden.
    periodHint: string;
  }>;
  skills: Array<{
    name: string;
    yearsExperience: string;
  }>;
  summary: string;
};

/**
 * Prefyller formen från den löst tolkade ParsedContentDto (null → tom sträng).
 * Parsern gissar ALDRIG datum (DQ3-3a) — alla strukturerade datum startar tomma
 * för användaren att fylla i. `periodHint` bär den råa tolkade perioden som en
 * ledtråd (visas civilt, aldrig som placeholder/exempel) och strippas ur payloaden.
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
    experiences: content.experiences.map((e) => ({
      company: e.organization ?? "",
      role: e.title ?? "",
      startDate: "",
      endDate: "",
      description: e.rawText ?? "",
      periodHint: e.period ?? "",
    })),
    educations: content.educations.map((e) => ({
      institution: e.institution ?? "",
      degree: e.degree ?? "",
      startDate: "",
      endDate: "",
      periodHint: e.period ?? "",
    })),
    skills: content.skills.map((s) => ({
      name: s,
      yearsExperience: "",
    })),
    summary: content.profile ?? "",
  };
}

// Råpayloaden matchar resumeContentSchema:s ingångsform (strängar/undefined in,
// null ut). `periodHint` är medvetet bortstrippad — den är display-only.
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
};

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
      endDate: e.endDate || undefined,
      description: e.description || undefined,
    })),
    educations: values.educations.map((e) => ({
      institution: e.institution,
      degree: e.degree,
      startDate: e.startDate,
      endDate: e.endDate || undefined,
    })),
    skills: values.skills.map((s) => ({
      name: s.name,
      yearsExperience:
        s.yearsExperience === ""
          ? null
          : Number.parseInt(s.yearsExperience, 10),
    })),
    summary: values.summary || undefined,
  };
}

type FieldError = { path: string | null; message: string };

const ERROR_ID = "gapfill-form-error";

/**
 * Mappar ett Zod-issue-path från promoteParsedResumeSchema till HTML-`id`:t på
 * motsvarande kontroll. Paths är prefixade (`name`, `content.<...>`) jämfört med
 * resumeContentSchema — `name` har egen kontroll, `content.`-prefixet strippas
 * och delegeras till den delade `pathToElementId`. `null` → ingen focus-flytt.
 */
function gapFillPathToElementId(path: string): string | null {
  if (path === "name") return "cv-name";
  if (path.startsWith("content.")) {
    return pathToElementId(path.slice("content.".length));
  }
  return null;
}

export function CvGapFillForm({
  parsedId,
  sourceFileName,
  content,
}: CvGapFillFormProps) {
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<FieldError | null>(null);

  const { register, control, handleSubmit, getValues } = useForm<FormValues>({
    defaultValues: toFormValues(content.contact.fullName ?? "", content),
    shouldUnregister: false,
  });

  const experiences = useFieldArray({ control, name: "experiences" });
  const educations = useFieldArray({ control, name: "educations" });
  const skills = useFieldArray({ control, name: "skills" });

  function fieldA11y(path: string) {
    return serverError?.path === path
      ? ({ "aria-invalid": true, "aria-describedby": ERROR_ID } as const)
      : {};
  }

  useEffect(() => {
    if (!serverError?.path) return;
    const elementId = gapFillPathToElementId(serverError.path);
    if (elementId) {
      document.getElementById(elementId)?.focus();
    }
  }, [serverError]);

  function onSubmit(values: FormValues) {
    setServerError(null);
    const rawPayload = toRawPayload(values);
    // Klient-validering speglar server-actionen (server-validering är auktoritativ).
    // Schemat validerar/transformerar ("" → null) till en ResumeContentDto-form.
    const parsed = promoteParsedResumeSchema.safeParse({
      parsedResumeId: parsedId,
      name: values.name,
      content: rawPayload,
    });
    if (!parsed.success) {
      const first = parsed.error.issues[0];
      if (first) {
        const path = first.path.join(".");
        setServerError({ path: path || null, message: first.message });
      } else {
        setServerError({ path: null, message: "Ogiltiga uppgifter." });
      }
      return;
    }
    startTransition(async () => {
      // Vid lyckad befordran kastar actionen NEXT_REDIRECT (→ /cv/{nytt-id}) —
      // det är en framgångssignal, inte ett fel, och får propagera ut. Bara ett
      // returnerat ActionResult med success:false hanteras som fel här.
      const result = await promoteParsedResumeAction(
        parsedId,
        parsed.data.name,
        parsed.data.content as ResumeContentDto
      );
      if (!result.success) {
        setServerError({ path: null, message: result.error });
      }
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-8">
      <section aria-label="CV-namn" className="flex flex-col gap-1.5">
        <Label htmlFor="cv-name">
          Namn på CV <span aria-hidden="true">*</span>
        </Label>
        <p id="cv-name-hint" className="text-body-sm text-text-secondary">
          Ett internt namn så att du hittar rätt CV-variant.
        </p>
        <Input
          id="cv-name"
          {...register("name")}
          aria-describedby="cv-name-hint"
          {...fieldA11y("name")}
          maxLength={200}
          required
          disabled={isPending}
        />
      </section>

      <section aria-label="Personuppgifter" className="flex flex-col gap-4">
        <h2 className="text-h3 font-medium text-text-primary">Personuppgifter</h2>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="pi-fullName">
              Fullständigt namn <span aria-hidden="true">*</span>
            </Label>
            <Input
              id="pi-fullName"
              {...register("personalInfo.fullName")}
              {...fieldA11y("content.personalInfo.fullName")}
              maxLength={200}
              required
              disabled={isPending}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="pi-email">E-post</Label>
            <Input
              id="pi-email"
              type="email"
              {...register("personalInfo.email")}
              {...fieldA11y("content.personalInfo.email")}
              disabled={isPending}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="pi-phone">Telefon</Label>
            <Input
              id="pi-phone"
              type="tel"
              {...register("personalInfo.phone")}
              {...fieldA11y("content.personalInfo.phone")}
              disabled={isPending}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="pi-location">Ort</Label>
            <Input
              id="pi-location"
              {...register("personalInfo.location")}
              {...fieldA11y("content.personalInfo.location")}
              disabled={isPending}
            />
          </div>
        </div>
      </section>

      <section className="flex flex-col gap-2">
        <h2 className="text-h3 font-medium text-text-primary">Sammanfattning</h2>
        <Label htmlFor="summary" className="sr-only">
          Sammanfattning
        </Label>
        <p id="summary-hint" className="text-body-sm text-text-secondary">
          En kort sammanfattning av din profil.
        </p>
        <Textarea
          id="summary"
          {...register("summary")}
          aria-describedby="summary-hint"
          {...fieldA11y("content.summary")}
          rows={4}
          maxLength={2000}
          disabled={isPending}
        />
      </section>

      <section aria-label="Erfarenhet" className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h2 className="text-h3 font-medium text-text-primary">Erfarenhet</h2>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => experiences.append(emptyExperienceFormItem())}
            disabled={isPending}
          >
            Lägg till erfarenhet
          </Button>
        </div>
        {experiences.fields.length === 0 && (
          <p className="text-body-sm text-text-secondary">
            Ingen erfarenhet tillagd.
          </p>
        )}
        <div className="flex flex-col gap-3">
          {experiences.fields.map((field, index) => {
            const periodHint = getValues(`experiences.${index}.periodHint`);
            const dateHintId = periodHint
              ? `exp-${index}-period-hint`
              : undefined;
            return (
              <fieldset
                key={field.id}
                className="flex flex-col gap-3 rounded-md border border-border bg-card p-4"
              >
                <legend className="sr-only">Erfarenhet {index + 1}</legend>
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`exp-${index}-company`}>
                      Företag <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id={`exp-${index}-company`}
                      {...register(`experiences.${index}.company`)}
                      {...fieldA11y(`content.experiences.${index}.company`)}
                      maxLength={200}
                      required
                      disabled={isPending}
                    />
                  </div>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`exp-${index}-role`}>
                      Roll <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id={`exp-${index}-role`}
                      {...register(`experiences.${index}.role`)}
                      {...fieldA11y(`content.experiences.${index}.role`)}
                      maxLength={200}
                      required
                      disabled={isPending}
                    />
                  </div>
                  <div
                    className="flex flex-col gap-1.5"
                    role="group"
                    aria-label="Period"
                    aria-describedby={dateHintId}
                  >
                    <Label htmlFor={`exp-${index}-startDate`}>
                      Startdatum <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id={`exp-${index}-startDate`}
                      type="date"
                      {...register(`experiences.${index}.startDate`)}
                      {...fieldA11y(`content.experiences.${index}.startDate`)}
                      required
                      disabled={isPending}
                    />
                    <Label htmlFor={`exp-${index}-endDate`}>
                      Slutdatum (valfritt)
                    </Label>
                    <Input
                      id={`exp-${index}-endDate`}
                      type="date"
                      {...register(`experiences.${index}.endDate`)}
                      {...fieldA11y(`content.experiences.${index}.endDate`)}
                      disabled={isPending}
                    />
                    {periodHint && (
                      <p
                        id={dateHintId}
                        className="text-body-sm text-text-secondary"
                      >
                        Tolkad period: {periodHint}
                      </p>
                    )}
                  </div>
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`exp-${index}-description`}>Beskrivning</Label>
                  <Textarea
                    id={`exp-${index}-description`}
                    {...register(`experiences.${index}.description`)}
                    {...fieldA11y(`content.experiences.${index}.description`)}
                    rows={3}
                    maxLength={2000}
                    disabled={isPending}
                  />
                </div>
                <div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => experiences.remove(index)}
                    aria-label={`Ta bort erfarenhet ${index + 1}`}
                    disabled={isPending}
                  >
                    Ta bort
                  </Button>
                </div>
              </fieldset>
            );
          })}
        </div>
      </section>

      <section aria-label="Utbildning" className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h2 className="text-h3 font-medium text-text-primary">Utbildning</h2>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => educations.append(emptyEducationFormItem())}
            disabled={isPending}
          >
            Lägg till utbildning
          </Button>
        </div>
        {educations.fields.length === 0 && (
          <p className="text-body-sm text-text-secondary">
            Ingen utbildning tillagd.
          </p>
        )}
        <div className="flex flex-col gap-3">
          {educations.fields.map((field, index) => {
            const periodHint = getValues(`educations.${index}.periodHint`);
            const dateHintId = periodHint
              ? `edu-${index}-period-hint`
              : undefined;
            return (
              <fieldset
                key={field.id}
                className="flex flex-col gap-3 rounded-md border border-border bg-card p-4"
              >
                <legend className="sr-only">Utbildning {index + 1}</legend>
                <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`edu-${index}-institution`}>
                      Lärosäte <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id={`edu-${index}-institution`}
                      {...register(`educations.${index}.institution`)}
                      {...fieldA11y(`content.educations.${index}.institution`)}
                      maxLength={200}
                      required
                      disabled={isPending}
                    />
                  </div>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`edu-${index}-degree`}>
                      Examen <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id={`edu-${index}-degree`}
                      {...register(`educations.${index}.degree`)}
                      {...fieldA11y(`content.educations.${index}.degree`)}
                      maxLength={200}
                      required
                      disabled={isPending}
                    />
                  </div>
                  <div
                    className="flex flex-col gap-1.5"
                    role="group"
                    aria-label="Period"
                    aria-describedby={dateHintId}
                  >
                    <Label htmlFor={`edu-${index}-startDate`}>
                      Startdatum <span aria-hidden="true">*</span>
                    </Label>
                    <Input
                      id={`edu-${index}-startDate`}
                      type="date"
                      {...register(`educations.${index}.startDate`)}
                      {...fieldA11y(`content.educations.${index}.startDate`)}
                      required
                      disabled={isPending}
                    />
                    <Label htmlFor={`edu-${index}-endDate`}>
                      Slutdatum (valfritt)
                    </Label>
                    <Input
                      id={`edu-${index}-endDate`}
                      type="date"
                      {...register(`educations.${index}.endDate`)}
                      {...fieldA11y(`content.educations.${index}.endDate`)}
                      disabled={isPending}
                    />
                    {periodHint && (
                      <p
                        id={dateHintId}
                        className="text-body-sm text-text-secondary"
                      >
                        Tolkad period: {periodHint}
                      </p>
                    )}
                  </div>
                </div>
                <div>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => educations.remove(index)}
                    aria-label={`Ta bort utbildning ${index + 1}`}
                    disabled={isPending}
                  >
                    Ta bort
                  </Button>
                </div>
              </fieldset>
            );
          })}
        </div>
      </section>

      <section aria-label="Färdigheter" className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <h2 className="text-h3 font-medium text-text-primary">Färdigheter</h2>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => skills.append(emptySkillFormItem())}
            disabled={isPending}
          >
            Lägg till färdighet
          </Button>
        </div>
        {skills.fields.length === 0 && (
          <p className="text-body-sm text-text-secondary">
            Inga färdigheter tillagda.
          </p>
        )}
        <div className="flex flex-col gap-2">
          {skills.fields.map((field, index) => (
            <fieldset
              key={field.id}
              className="grid grid-cols-1 items-start gap-3 rounded-md border border-border bg-card p-4 md:grid-cols-[1fr_140px_auto]"
            >
              <legend className="sr-only">Färdighet {index + 1}</legend>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor={`skill-${index}-name`}>
                  Namn <span aria-hidden="true">*</span>
                </Label>
                <Input
                  id={`skill-${index}-name`}
                  {...register(`skills.${index}.name`)}
                  {...fieldA11y(`content.skills.${index}.name`)}
                  maxLength={100}
                  required
                  disabled={isPending}
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor={`skill-${index}-years`}>År (valfritt)</Label>
                <Controller
                  control={control}
                  name={`skills.${index}.yearsExperience`}
                  render={({ field: ctlField }) => (
                    <Input
                      id={`skill-${index}-years`}
                      type="number"
                      step={1}
                      min={0}
                      max={70}
                      value={ctlField.value}
                      onChange={(e) => ctlField.onChange(e.target.value)}
                      onBlur={ctlField.onBlur}
                      name={ctlField.name}
                      {...fieldA11y(`content.skills.${index}.yearsExperience`)}
                      disabled={isPending}
                    />
                  )}
                />
              </div>
              <div className="self-end">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => skills.remove(index)}
                  aria-label={`Ta bort färdighet ${index + 1}`}
                  disabled={isPending}
                >
                  Ta bort
                </Button>
              </div>
            </fieldset>
          ))}
        </div>
      </section>

      <div className="flex flex-col gap-3 border-t border-border pt-6">
        {serverError && (
          <p
            id={ERROR_ID}
            className="text-body-sm text-danger-600"
            role="alert"
          >
            {serverError.message}
          </p>
        )}
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={isPending}>
            {isPending ? "Sparar…" : "Spara CV"}
          </Button>
          <Button asChild variant="ghost">
            <Link href={`/cv/granska/${parsedId}`}>Avbryt</Link>
          </Button>
        </div>
        <p className="text-body-sm text-text-secondary">
          Källfil: <span className="font-mono">{sourceFileName}</span>
        </p>
      </div>
    </form>
  );
}

function emptyExperienceFormItem() {
  return {
    company: "",
    role: "",
    startDate: "",
    endDate: "",
    description: "",
    periodHint: "",
  };
}

function emptyEducationFormItem() {
  return {
    institution: "",
    degree: "",
    startDate: "",
    endDate: "",
    periodHint: "",
  };
}

function emptySkillFormItem() {
  return {
    name: "",
    yearsExperience: "",
  };
}
