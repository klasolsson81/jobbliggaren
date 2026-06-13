import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { env } from "@/lib/env";
import { currentUserSchema } from "@/lib/dto/me";
import { parseResponse } from "@/lib/dto/_helpers";

export async function GET() {
  const cookieStore = await cookies();
  const sessionId = cookieStore.get("__Host-jobbliggaren_session")?.value;

  if (!sessionId) {
    return NextResponse.json(null, { status: 401 });
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me`, {
      headers: { Authorization: `Bearer ${sessionId}` },
      cache: "no-store",
    });

    if (!res.ok) {
      return NextResponse.json(null, { status: res.status });
    }

    const user = await parseResponse(
      res,
      currentUserSchema,
      "GET /api/v1/me (proxy)"
    );
    return NextResponse.json(user);
  } catch {
    // DtoParseError + network errors both surface as 503 upstream-unavailable
    return NextResponse.json(null, { status: 503 });
  }
}
