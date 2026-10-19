import {
  emailDeliveryPeriodSchema,
  type EmailDeliveryDto,
  type EmailDeliveryPeriod,
  type EmailFailuresDto,
} from "@/lib/dto/admin-email";
import { allowanceFailureGroup, type AllowanceFailureGroup } from "./email-allowance";

/**
 * What the e-post page's Leverans section and the overview's attention line read from a delivery read (#1981 PR 2).
 * Both say the same thing about the same state, so both take it from here.
 */
export const EMAIL_DELIVERY_PERIODS: ReadonlyArray<EmailDeliveryPeriod> = emailDeliveryPeriodSchema.options;

/** The period a page without `?period=` shows, and the one a value outside the three falls back to. */
export const DEFAULT_EMAIL_DELIVERY_PERIOD: EmailDeliveryPeriod = "d7";

/** The period the overview's attention line reads; its link opens the page on the same period. */
export const ATTENTION_EMAIL_DELIVERY_PERIOD: EmailDeliveryPeriod = "h24";

/** `?period=` as the page reads it: one of the three, else 7 dygn. A repeated parameter counts by its first value. */
export function emailDeliveryPeriod(value: string | ReadonlyArray<string> | undefined): EmailDeliveryPeriod {
  const parsed = emailDeliveryPeriodSchema.safeParse(typeof value === "string" ? value : value?.[0]);
  return parsed.success ? parsed.data : DEFAULT_EMAIL_DELIVERY_PERIOD;
}

/**
 * A path with its period, as the page's links and the reads behind them name it. Every period is written out, the
 * default too, so each link names its own view.
 */
export function withEmailPeriod(path: string, period: EmailDeliveryPeriod): string {
  return `${path}?${new URLSearchParams({ period })}`;
}

/**
 * Why a delivery read shows no figures, as the key of its line under `admin.email.allowance`, where every provider
 * read's lines live. Null for an observed read, which shows its figures instead.
 */
export type EmailReadLine = "notConfigured" | "notObservable" | "readKeyMissing" | `failure.${AllowanceFailureGroup}`;

export function emailReadLine(read: EmailDeliveryDto | EmailFailuresDto): EmailReadLine | null {
  switch (read.status) {
    case "Observed":
      return null;
    case "NotObserved":
      return "notConfigured";
    case "NotObservable":
      return "notObservable";
    case "ReadKeyMissing":
      return "readKeyMissing";
    case "Failed":
      return `failure.${allowanceFailureGroup(read.reason)}`;
  }
}

/** A provider state the reader must act on, or cannot judge: announced as an alert, never only shown. */
export function isAlertingRead(read: EmailDeliveryDto | EmailFailuresDto): boolean {
  return read.status === "NotObservable" || read.status === "ReadKeyMissing" || read.status === "Failed";
}

/**
 * A row's time to the minute, as the page shows it and as its `<time>` carries it: the seconds the provider recorded
 * are not shown anywhere. An ISO instant in UTC without seconds, which `dateTime` accepts.
 */
export function failureMinute(createdAt: string): string {
  const ms = Date.parse(createdAt);
  return new Date(ms - (((ms % 60_000) + 60_000) % 60_000)).toISOString().slice(0, 16) + "Z";
}
