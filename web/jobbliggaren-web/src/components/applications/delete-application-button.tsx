"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { DeleteApplicationDialog } from "./delete-application-dialog";

/**
 * #782 (ADR 0104) — detail-footer "Ta bort ansökan" affordance. Self-contained
 * (its own open state + a single dialog instance — one per detail page, so the
 * island-level "never N dialogs" rule does not apply here), mirroring
 * WithdrawApplicationButton's footer idiom and reusing the shared
 * DeleteApplicationDialog body. Unlike Withdraw it is ALWAYS available (deletion
 * has no transition precondition). On success the application no longer exists, so
 * we navigate back to the list rather than re-render the dead detail.
 */
export function DeleteApplicationButton({
  applicationId,
}: {
  applicationId: string;
}) {
  const tUi = useTranslations("applications.ui");
  const router = useRouter();
  const [open, setOpen] = useState(false);

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="text-danger-700"
        onClick={() => setOpen(true)}
      >
        {tUi("delete.action")}
      </Button>
      <DeleteApplicationDialog
        open={open}
        onOpenChange={setOpen}
        applicationId={applicationId}
        onDeleted={() => router.push("/ansokningar")}
      />
    </>
  );
}
