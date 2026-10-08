import "server-only";

import type { z } from "zod";
import { isFeedbackId } from "@/lib/admin/feedback";
import type { AdminFeedbackWindow } from "@/lib/admin/view-models";
import { getSessionId } from "@/lib/auth/session";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import {
  feedbackAvailabilitySchema,
  feedbackDetailSchema,
  feedbackListResponseSchema,
  feedbackSummarySchema,
  type FeedbackAvailabilityDto,
  type FeedbackDetailDto,
  type FeedbackListCriteria,
  type FeedbackListResponse,
  type FeedbackSummaryDto,
} from "@/lib/dto/admin-feedback";
import { authedFetch } from "@/lib/http/authed-fetch";

const BASE_PATH = "/api/v1/admin/feedback";

/** The path of one submission, its status and its notice's requeue share. */
export function feedbackPath(id: string): string {
  return `${BASE_PATH}/${encodeURIComponent(id)}`;
}

/** The list's query string: a missing status or page is left out, so the backend reads it as no filter. */
export function feedbackListQuery(criteria: FeedbackListCriteria): string {
  const params = new URLSearchParams();
  if (criteria.status !== undefined) params.set("status", criteria.status);
  if (criteria.page !== undefined) params.set("page", criteria.page);
  params.set("pageNumber", String(criteria.pageNumber));
  params.set("pageSize", String(criteria.pageSize));
  return params.toString();
}

async function read<T>(
  path: string,
  schema: z.ZodType<T>,
  context: string,
  includeNotFound = false,
): Promise<ApiResult<T>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, path);
    return await responseToResult(res, schema, context, { includeNotFound });
  } catch {
    return { kind: "error" };
  }
}

/**
 * One page of submissions, newest first, with the counts per status inside the page filter (#1979).
 * The filters are a status and a page key, neither of which names a person, so they travel in the query.
 */
export function listFeedback(criteria: FeedbackListCriteria): Promise<ApiResult<FeedbackListResponse>> {
  return read(`${BASE_PATH}?${feedbackListQuery(criteria)}`, feedbackListResponseSchema, "GET /api/v1/admin/feedback");
}

/** One submission, with the reporter's address read on the server, or `notFound` when there is none with the id. */
export async function getFeedbackDetail(id: string): Promise<ApiResult<FeedbackDetailDto>> {
  if (!isFeedbackId(id)) return { kind: "notFound" };
  return read(feedbackPath(id), feedbackDetailSchema, "GET /api/v1/admin/feedback/{id}", true);
}

/** The ratings per page over the last 7, 30 or 90 days. */
export function getFeedbackSummary(days: AdminFeedbackWindow): Promise<ApiResult<FeedbackSummaryDto>> {
  return read(`${BASE_PATH}/summary?days=${days}`, feedbackSummarySchema, "GET /api/v1/admin/feedback/summary");
}

/** Why feedback is open or closed. */
export function getFeedbackAvailability(): Promise<ApiResult<FeedbackAvailabilityDto>> {
  return read(`${BASE_PATH}/availability`, feedbackAvailabilitySchema, "GET /api/v1/admin/feedback/availability");
}
