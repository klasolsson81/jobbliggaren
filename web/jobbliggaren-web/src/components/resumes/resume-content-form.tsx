"use client";

import { useEffect, useState, useTransition } from "react";
import { useForm, useFieldArray, Controller } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { resumeContentSchema } from "@/lib/actions/resume-schemas";
import {
  emptyEducation,
  emptyExperience,
  emptySkill,
} from "@/lib/resumes/content-utils";
import { updateMasterContentAction } from "@/lib/actions/resumes";
import { pathToElementId } from "@/lib/forms/resume-path-routing";
import type { ResumeContentDto } from "@/lib/types/resumes";

interface ResumeContentFormProps {
  resumeId: string;
  initialContent: ResumeContentDto;
}

type FormValues = {
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
  }>;
  educations: Array<{
    institution: string;
    degree: string;
    startDate: string;
    endDate: string;
  }>;
  skills: Array<{
    name: string;
    yearsExperience: string;
  }>;
  summary: string;
};

function toFormValues(content: ResumeContentDto): FormValues {
  return {
    personalInfo: {
      fullName: content.personalInfo.fullName ?? "",
      email: content.personalInfo.email ?? "",
      phone: content.personalInfo.phone ?? "",
      location: content.personalInfo.location ?? "",
    },
    experiences: content.experiences.map((e) => ({
      company: e.company,
      role: e.role,
      startDate: e.startDate,
      endDate: e.endDate ?? "",
      description: e.description ?? "",
    })),
    educations: content.educations.map((e) => ({
      institution: e.institution,
      degree: e.degree,
      startDate: e.startDate,
      endDate: e.endDate ?? "",
    })),
    skills: content.skills.map((s) => ({
      name: s.name,
      yearsExperience:
        s.yearsExperience === null ? "" : String(s.yearsExperience),
    })),
    summary: content.summary ?? "",
  };
}

// Build the raw payload for the schema. The schema's transform-pipes
// expect strings (or undefined) as input and emit null as output.
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

const ERROR_ID = "content-form-error";

export function ResumeContentForm({
  resumeId,
  initialContent,
}: ResumeContentFormProps) {
  const [isPending, startTransition] = useTransition();
  const [savedAt, setSavedAt] = useState<Date | null>(null);
  const [serverError, setServerError] = useState<FieldError | null>(null);

  const { register, control, handleSubmit } = useForm<FormValues>({
    defaultValues: toFormValues(initialContent),
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
    const elementId = pathToElementId(serverError.path);
    if (elementId) {
      document.getElementById(elementId)?.focus();
    }
  }, [serverError]);

  function onSubmit(values: FormValues) {
    setServerError(null);
    setSavedAt(null);
    const payload = toRawPayload(values);
    // Schema validates and transforms ("" → null) into a ResumeContentDto-compatible shape.
    const parsed = resumeContentSchema.safeParse(payload);
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
      const result = await updateMasterContentAction(
        resumeId,
        parsed.data as ResumeContentDto
      );
      if (!result.success) {
        setServerError({ path: null, message: result.error });
        return;
      }
      setSavedAt(new Date());
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-8">
      <section aria-label="Personuppgifter" className="flex flex-col gap-4">
        <h2 className="text-h3 font-medium text-text-primary">Personuppgifter</h2>
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="pi-fullName">Fullständigt namn</Label>
            <Input
              id="pi-fullName"
              {...register("personalInfo.fullName")}
              {...fieldA11y("personalInfo.fullName")}
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
              {...fieldA11y("personalInfo.email")}
              disabled={isPending}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="pi-phone">Telefon</Label>
            <Input
              id="pi-phone"
              type="tel"
              {...register("personalInfo.phone")}
              {...fieldA11y("personalInfo.phone")}
              disabled={isPending}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="pi-location">Ort</Label>
            <Input
              id="pi-location"
              {...register("personalInfo.location")}
              {...fieldA11y("personalInfo.location")}
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
          {...fieldA11y("summary")}
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
          {experiences.fields.map((field, index) => (
            <fieldset
              key={field.id}
              className="flex flex-col gap-3 rounded-md border border-border bg-card p-4"
            >
              <legend className="sr-only">Erfarenhet {index + 1}</legend>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`exp-${index}-company`}>Företag</Label>
                  <Input
                    id={`exp-${index}-company`}
                    {...register(`experiences.${index}.company`)}
                    {...fieldA11y(`experiences.${index}.company`)}
                    maxLength={200}
                    required
                    disabled={isPending}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`exp-${index}-role`}>Roll</Label>
                  <Input
                    id={`exp-${index}-role`}
                    {...register(`experiences.${index}.role`)}
                    {...fieldA11y(`experiences.${index}.role`)}
                    maxLength={200}
                    required
                    disabled={isPending}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`exp-${index}-startDate`}>Startdatum</Label>
                  <Input
                    id={`exp-${index}-startDate`}
                    type="date"
                    {...register(`experiences.${index}.startDate`)}
                    {...fieldA11y(`experiences.${index}.startDate`)}
                    required
                    disabled={isPending}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`exp-${index}-endDate`}>
                    Slutdatum (valfritt)
                  </Label>
                  <Input
                    id={`exp-${index}-endDate`}
                    type="date"
                    {...register(`experiences.${index}.endDate`)}
                    {...fieldA11y(`experiences.${index}.endDate`)}
                    disabled={isPending}
                  />
                </div>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor={`exp-${index}-description`}>Beskrivning</Label>
                <Textarea
                  id={`exp-${index}-description`}
                  {...register(`experiences.${index}.description`)}
                  {...fieldA11y(`experiences.${index}.description`)}
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
          ))}
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
          {educations.fields.map((field, index) => (
            <fieldset
              key={field.id}
              className="flex flex-col gap-3 rounded-md border border-border bg-card p-4"
            >
              <legend className="sr-only">Utbildning {index + 1}</legend>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`edu-${index}-institution`}>Lärosäte</Label>
                  <Input
                    id={`edu-${index}-institution`}
                    {...register(`educations.${index}.institution`)}
                    {...fieldA11y(`educations.${index}.institution`)}
                    maxLength={200}
                    required
                    disabled={isPending}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`edu-${index}-degree`}>Examen</Label>
                  <Input
                    id={`edu-${index}-degree`}
                    {...register(`educations.${index}.degree`)}
                    {...fieldA11y(`educations.${index}.degree`)}
                    maxLength={200}
                    required
                    disabled={isPending}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`edu-${index}-startDate`}>Startdatum</Label>
                  <Input
                    id={`edu-${index}-startDate`}
                    type="date"
                    {...register(`educations.${index}.startDate`)}
                    {...fieldA11y(`educations.${index}.startDate`)}
                    required
                    disabled={isPending}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={`edu-${index}-endDate`}>
                    Slutdatum (valfritt)
                  </Label>
                  <Input
                    id={`edu-${index}-endDate`}
                    type="date"
                    {...register(`educations.${index}.endDate`)}
                    {...fieldA11y(`educations.${index}.endDate`)}
                    disabled={isPending}
                  />
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
          ))}
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
                <Label htmlFor={`skill-${index}-name`}>Namn</Label>
                <Input
                  id={`skill-${index}-name`}
                  {...register(`skills.${index}.name`)}
                  {...fieldA11y(`skills.${index}.name`)}
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
                      value={ctlField.value}
                      onChange={(e) => ctlField.onChange(e.target.value)}
                      onBlur={ctlField.onBlur}
                      name={ctlField.name}
                      {...fieldA11y(`skills.${index}.yearsExperience`)}
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

      <div className="flex items-center gap-3 border-t border-border pt-6">
        <Button type="submit" disabled={isPending}>
          {isPending ? "Sparar..." : "Spara CV"}
        </Button>
        {savedAt && !serverError && (
          <p className="text-body-sm text-text-secondary" role="status">
            Sparat {savedAt.toLocaleTimeString("sv-SE")}.
          </p>
        )}
        {serverError && (
          <p
            id={ERROR_ID}
            className="text-body-sm text-danger-600"
            role="alert"
          >
            {serverError.message}
          </p>
        )}
      </div>
    </form>
  );
}

function emptyExperienceFormItem() {
  const e = emptyExperience();
  return {
    company: e.company,
    role: e.role,
    startDate: e.startDate,
    endDate: e.endDate ?? "",
    description: e.description ?? "",
  };
}

function emptyEducationFormItem() {
  const e = emptyEducation();
  return {
    institution: e.institution,
    degree: e.degree,
    startDate: e.startDate,
    endDate: e.endDate ?? "",
  };
}

function emptySkillFormItem() {
  const s = emptySkill();
  return {
    name: s.name,
    yearsExperience: s.yearsExperience === null ? "" : String(s.yearsExperience),
  };
}
