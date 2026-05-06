import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import type { CurrentUser } from "@/lib/auth/session";
import { env } from "@/lib/env";

export async function GET() {
  const cookieStore = await cookies();
  const sessionId = cookieStore.get("__Host-jobbpilot_session")?.value;

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

    const user = (await res.json()) as CurrentUser;
    return NextResponse.json(user);
  } catch {
    return NextResponse.json(null, { status: 503 });
  }
}
