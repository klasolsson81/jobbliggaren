"use client";

// On-demand image reads and thumbnail/full-size controls require client interaction.

import { useEffect, useId, useState } from "react";
import { useTranslations } from "next-intl";
import type { AdminFeedbackItem } from "@/lib/admin/view-models";
import { MAX_FEEDBACK_SCREENSHOT_BYTES } from "@/lib/admin/feedback";

type ImageState =
  | { readonly kind: "loading" | "absent" | "failed"; readonly id: string }
  | { readonly kind: "ready"; readonly id: string; readonly url: string };

function ScreenshotContent({
  id,
  metadata,
}: {
  readonly id: string;
  readonly metadata: AdminFeedbackItem["screenshot"];
}) {
  const t = useTranslations("admin.feedback.detail.screenshot");
  const imageId = useId();
  const [state, setState] = useState<ImageState>({ kind: "loading", id });
  const [fullSize, setFullSize] = useState(false);
  const hasImage = metadata !== null;

  useEffect(() => {
    if (!hasImage) return;
    const controller = new AbortController();
    let url: string | null = null;
    void (async () => {
      try {
        const response = await fetch("/api/admin/feedback/screenshot", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ id }),
          cache: "no-store",
          signal: controller.signal,
        });
        if (controller.signal.aborted) return;
        if (response.status === 404) {
          setState({ kind: "absent", id });
          return;
        }
        if (!response.ok) throw new Error("Screenshot read refused");
        const blob = await response.blob();
        if (controller.signal.aborted) return;
        if (blob.type !== "image/png" || blob.size === 0 || blob.size > MAX_FEEDBACK_SCREENSHOT_BYTES) {
          throw new Error("Invalid screenshot response");
        }
        url = URL.createObjectURL(blob);
        setState({ kind: "ready", id, url });
      } catch {
        if (!controller.signal.aborted) setState({ kind: "failed", id });
      }
    })();
    return () => {
      controller.abort();
      if (url !== null) URL.revokeObjectURL(url);
    };
  }, [id, hasImage]);

  const current: ImageState = !hasImage ? { kind: "absent", id } : state.id === id ? state : { kind: "loading", id };
  return (
    <section className="jp-adminfeedback__block" aria-label={t("heading")}>
      <h3 className="jp-adminfeedback__subhead">{t("heading")}</h3>
      <p className={current.kind === "ready" ? "sr-only" : "jp-adminfeedback__none"} role="status" aria-atomic="true">
        {current.kind !== "ready"
          ? t(current.kind === "loading" ? "loading" : current.kind === "absent" ? "absent" : "failed")
          : null}
      </p>
      {current.kind === "ready" ? (
        <>
          <button
            type="button"
            className="jp-btn jp-btn--secondary jp-btn--sm"
            aria-expanded={fullSize}
            aria-controls={imageId}
            onClick={() => setFullSize((value) => !value)}
          >
            {t(fullSize ? "showThumbnail" : "showFullSize")}
          </button>
          <div
            id={imageId}
            className="jp-adminfeedback__image"
            data-full-size={fullSize}
            role="region"
            aria-label={t("alt")}
            tabIndex={fullSize ? 0 : undefined}
          >
            {/* Private blob URLs stay in this browser and cannot use the image optimisation proxy. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={current.url}
              alt={t("alt")}
              width={metadata?.width}
              height={metadata?.height}
              onError={() => setState({ kind: "failed", id })}
            />
          </div>
        </>
      ) : null}
    </section>
  );
}

export function AdminFeedbackScreenshot(props: {
  readonly id: string;
  readonly metadata: AdminFeedbackItem["screenshot"];
}) {
  return <ScreenshotContent key={`${props.id}:${props.metadata !== null}`} {...props} />;
}
