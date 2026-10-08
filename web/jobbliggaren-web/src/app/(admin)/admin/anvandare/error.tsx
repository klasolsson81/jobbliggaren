"use client";

import type { ErrorInfo } from "next/error";
import { AdminErrorSurface } from "@/components/admin/admin-error-surface";
import { useReloadOnStaleBuild } from "@/lib/hooks/use-reload-on-stale-build";

export default function AdminAccountsError({ error, retry }: ErrorInfo) {
  const reloading = useReloadOnStaleBuild(error);
  if (reloading) return null;

  return <AdminErrorSurface retry={retry} />;
}
