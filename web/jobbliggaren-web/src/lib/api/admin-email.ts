import "server-only";

import type { z } from "zod";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { responseToResult } from "@/lib/dto/_helpers";
import {
  emailAllowanceSchema,
  emailDeliverySchema,
  emailDmarcSchema,
  emailFailuresSchema,
  emailKindsSchema,
  type EmailAllowanceDto,
  type EmailDeliveryDto,
  type EmailDeliveryPeriod,
  type EmailDmarcDto,
  type EmailFailuresDto,
  type EmailKindsDto,
} from "@/lib/dto/admin-email";
import { withEmailPeriod } from "@/lib/admin/email-delivery";
import { SOURCE_DEADLINE_MS } from "./admin-overview";

export const EMAIL_ALLOWANCE_PATH = "/api/v1/admin/email/allowance";
export const EMAIL_DELIVERY_PATH = "/api/v1/admin/email/delivery";
export const EMAIL_FAILURES_PATH = "/api/v1/admin/email/failures";
export const EMAIL_KINDS_PATH = "/api/v1/admin/email/kinds";
export const EMAIL_DMARC_PATH = "/api/v1/admin/email/dmarc";

/** One e-post read's outcome: the body as the contract states it, a refusal, or anything else as an error. */
export type EmailRead<T> =
  | { readonly kind: "ok"; readonly data: T }
  | { readonly kind: "unauthorized" }
  | { readonly kind: "forbidden" }
  | { readonly kind: "error" };

export type EmailAllowanceRead = EmailRead<EmailAllowanceDto>;
export type EmailDeliveryRead = EmailRead<EmailDeliveryDto>;
export type EmailFailuresRead = EmailRead<EmailFailuresDto>;
export type EmailKindsRead = EmailRead<EmailKindsDto>;
export type EmailDmarcRead = EmailRead<EmailDmarcDto>;

/**
 * One of the e-post page's reads. It never throws: a refusal, a timeout, a status the contract does not name or a body
 * outside it is a result, which the page renders as that region's own failed state, so a failed read never reaches
 * the route's error boundary and never blanks the rest of the page (ADR 0150 D2).
 */
async function readEmail<T>(path: string, schema: z.ZodType<T>, signal: AbortSignal | undefined): Promise<EmailRead<T>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  const deadline = AbortSignal.timeout(SOURCE_DEADLINE_MS);
  const linked = signal ? AbortSignal.any([signal, deadline]) : deadline;
  try {
    const response = await authedFetch(sessionId, path, { signal: linked });
    const result = await responseToResult(response, schema, `GET ${path}`);
    switch (result.kind) {
      case "ok":
      case "unauthorized":
      case "forbidden":
        return result;
      default:
        return { kind: "error" };
    }
  } catch {
    return { kind: "error" };
  }
}

/**
 * The month against the free allowance (#1981 PR 1, ADR 0178). When the provider answered is `usage.observedAt` in the
 * body; the page needs no response header.
 */
export function loadEmailAllowance(signal?: AbortSignal): Promise<EmailAllowanceRead> {
  return readEmail(EMAIL_ALLOWANCE_PATH, emailAllowanceSchema, signal);
}

/** An answer for another period than the one asked for is not this page's answer, and reads as an error. */
function forPeriod<T extends { readonly period: EmailDeliveryPeriod }>(
  read: EmailRead<T>,
  period: EmailDeliveryPeriod,
): EmailRead<T> {
  return read.kind === "ok" && read.data.period !== period ? { kind: "error" } : read;
}

/** Leverans's sent and failed counts for one period (#1981 PR 2). The page sends the period and never a time. */
export async function loadEmailDelivery(period: EmailDeliveryPeriod, signal?: AbortSignal): Promise<EmailDeliveryRead> {
  return forPeriod(await readEmail(withEmailPeriod(EMAIL_DELIVERY_PATH, period), emailDeliverySchema, signal), period);
}

/**
 * The latest failed sends in one period (#1981 PR 2): time, masked recipient and reason. The page sends the period
 * and never a time or a size; the API decides both.
 */
export async function loadEmailFailures(period: EmailDeliveryPeriod, signal?: AbortSignal): Promise<EmailFailuresRead> {
  return forPeriod(await readEmail(withEmailPeriod(EMAIL_FAILURES_PATH, period), emailFailuresSchema, signal), period);
}

/**
 * Our own count of handovers to the provider per mail kind in one period (#1981 PR 3). Our database answers it, not the
 * provider; the page sends the period and never a time.
 */
export async function loadEmailKinds(period: EmailDeliveryPeriod, signal?: AbortSignal): Promise<EmailKindsRead> {
  return forPeriod(await readEmail(withEmailPeriod(EMAIL_KINDS_PATH, period), emailKindsSchema, signal), period);
}

/**
 * The DMARC aggregate reports over the region's own fixed window (#1981 PR 4b, ADR 0185 D9). The page sends no period:
 * Leverans's choice is not this region's, and the API decides the window.
 */
export function loadEmailDmarc(signal?: AbortSignal): Promise<EmailDmarcRead> {
  return readEmail(EMAIL_DMARC_PATH, emailDmarcSchema, signal);
}
