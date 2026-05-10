import "server-only";

import { cache } from "react";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import type { JobSeekerProfileDto } from "@/lib/types/me";

export const getMyProfile = cache(
  async (): Promise<JobSeekerProfileDto | null> => {
    const sessionId = await getSessionId();
    if (!sessionId) return null;

    try {
      const res = await fetch(`${env.BACKEND_URL}/api/v1/me/profile`, {
        headers: { Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
      if (!res.ok) return null;
      return (await res.json()) as JobSeekerProfileDto;
    } catch {
      return null;
    }
  }
);
