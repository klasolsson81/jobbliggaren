import "server-only";

import { cache } from "react";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  jobSeekerProfileSchema,
  type JobSeekerProfileDto,
} from "@/lib/dto/me";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

export const getMyProfile = cache(
  async (): Promise<ApiResult<JobSeekerProfileDto>> => {
    const sessionId = await getSessionId();
    if (!sessionId) return { kind: "unauthorized" };

    try {
      const res = await fetch(`${env.BACKEND_URL}/api/v1/me/profile`, {
        headers: { Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
      return await responseToResult(
        res,
        jobSeekerProfileSchema,
        "GET /api/v1/me/profile"
      );
    } catch {
      return { kind: "error" };
    }
  }
);
