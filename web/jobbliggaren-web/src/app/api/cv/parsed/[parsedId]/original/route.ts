import type { NextRequest, NextResponse } from "next/server";
import { proxyOriginalFile } from "@/lib/http/original-file-proxy";

/**
 * BFF for the user's OWN uploaded CV file, keyed on the import staging id — the id
 * `/cv/granska/[parsedId]` routes on, and the one the import response returns. Posture, error
 * mapping and header construction all live in `proxyOriginalFile`; this file exists only to bind
 * the route's id form to the backend path.
 */

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(
  request: NextRequest,
  ctx: { params: Promise<{ parsedId: string }> }
): Promise<NextResponse> {
  const { parsedId } = await ctx.params;
  return proxyOriginalFile(
    request,
    parsedId,
    (safeId) => `/api/v1/resumes/parsed/${safeId}/original`
  );
}
