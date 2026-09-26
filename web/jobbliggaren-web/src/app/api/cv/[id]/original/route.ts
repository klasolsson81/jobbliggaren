import type { NextRequest, NextResponse } from "next/server";
import { proxyOriginalFile } from "@/lib/http/original-file-proxy";

/**
 * BFF for the user's OWN uploaded CV file, keyed on a promoted, canonical Resume id — the id the
 * CV hub's cards carry. Posture, error mapping and header construction all live in
 * `proxyOriginalFile`; this file exists only to bind the route's id form to the backend path.
 */

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(
  request: NextRequest,
  ctx: { params: Promise<{ id: string }> }
): Promise<NextResponse> {
  const { id } = await ctx.params;
  return proxyOriginalFile(request, id, (safeId) => `/api/v1/resumes/${safeId}/original`);
}
