"use client";

// "use client": a file picker, a blob URL for the preview, and focus that has to follow a control
// which unmounts.

import { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { ImagePlus, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import type { ScreenshotRefusal, ScreenshotState } from "./use-feedback-form";

/** The formats the browser can redraw and the backend decodes. */
export const ACCEPTED_SCREENSHOT_TYPES: ReadonlyArray<string> = ["image/png", "image/jpeg", "image/webp"];

export const SCREENSHOT_REFUSAL_MESSAGE = {
  type: "screenshot.wrongType",
  unreadable: "screenshot.unreadable",
  tooLarge: "screenshot.tooLarge",
  tooDetailed: "screenshot.tooDetailed",
} as const satisfies Record<ScreenshotRefusal, string>;

/**
 * The image a paste into the feedback form carries, or null when the paste is text (C12). A paste with
 * any text in it is the user's text, even when the clipboard also holds a picture of it, as a copy from
 * an office program does; only an image-only paste becomes the screenshot.
 */
export function pastedScreenshot(data: DataTransfer): File | null {
  if (data.getData("text/plain") !== "") return null;
  const accepted = (file: File | null): file is File =>
    file !== null && ACCEPTED_SCREENSHOT_TYPES.includes(file.type);
  const fromFiles = Array.from(data.files).find(accepted);
  if (fromFiles !== undefined) return fromFiles;
  for (const item of Array.from(data.items)) {
    if (item.kind !== "file") continue;
    const file = item.getAsFile();
    if (accepted(file)) return file;
  }
  return null;
}

/** A blob URL for the preview, made in an effect and revoked when the image changes or the field goes. */
function usePreviewUrl(blob: Blob | null): string | null {
  const [preview, setPreview] = useState<{ blob: Blob; url: string } | null>(null);
  useEffect(() => {
    if (blob === null) return;
    const url = URL.createObjectURL(blob);
    let live = true;
    // Deferred so the effect body sets no state (react-hooks/set-state-in-effect).
    queueMicrotask(() => {
      if (live) setPreview({ blob, url });
    });
    return () => {
      live = false;
      URL.revokeObjectURL(url);
    };
  }, [blob]);
  return preview !== null && preview.blob === blob ? preview.url : null;
}

/**
 * The optional screenshot (#1979 PR3). A picked or pasted image is redrawn in the browser before it is
 * shown or sent (`prepareScreenshot`); a refusal is shown beside the control and the rating and text
 * stay as they are. The form's live region announces the preparing and the refusal; the text here is
 * for the eye and for the button's description.
 */
export function ScreenshotField({
  screenshot,
  onAdd,
  onRemove,
}: {
  screenshot: ScreenshotState;
  onAdd: (file: Blob) => void;
  onRemove: () => void;
}) {
  const t = useTranslations("feedback");
  const hintId = useId();
  const noteId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const addRef = useRef<HTMLButtonElement>(null);
  const removeRef = useRef<HTMLButtonElement>(null);
  const ready = screenshot.kind === "ready";
  const previewUrl = usePreviewUrl(ready ? screenshot.blob : null);

  // The add and remove buttons replace each other. When the one that had focus unmounts, focus goes to
  // the other one, and only then: a user who has moved on, say into the comment, keeps their place.
  const wasReady = useRef(ready);
  useLayoutEffect(() => {
    if (wasReady.current === ready) return;
    wasReady.current = ready;
    const active = document.activeElement;
    if (active !== null && active !== document.body) return;
    (ready ? removeRef : addRef).current?.focus();
  });

  const note =
    screenshot.kind === "preparing"
      ? t("screenshot.preparing")
      : screenshot.kind === "refused"
        ? t(SCREENSHOT_REFUSAL_MESSAGE[screenshot.reason])
        : null;

  return (
    <div className="jp-feedback__shot">
      <input
        ref={inputRef}
        type="file"
        accept={ACCEPTED_SCREENSHOT_TYPES.join(",")}
        hidden
        onChange={(event) => {
          const file = event.target.files?.[0];
          // Cleared so picking the same file again is still a change.
          event.target.value = "";
          if (file !== undefined) onAdd(file);
        }}
      />
      {ready ? (
        <div className="jp-feedback__shotready">
          {previewUrl !== null && (
            // A blob URL stays in this browser and cannot go through the image optimiser.
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={previewUrl}
              alt={t("screenshot.previewAlt")}
              width={screenshot.width}
              height={screenshot.height}
              className="jp-feedback__thumb"
            />
          )}
          <button ref={removeRef} type="button" className="jp-btn jp-btn--secondary jp-btn--sm" onClick={onRemove}>
            <Trash2 size={16} aria-hidden="true" />
            {t("screenshot.remove")}
          </button>
        </div>
      ) : (
        <div className="jp-feedback__shotpick">
          <button
            ref={addRef}
            type="button"
            className="jp-btn jp-btn--secondary jp-btn--sm"
            aria-describedby={note !== null ? `${noteId} ${hintId}` : hintId}
            onClick={() => inputRef.current?.click()}
          >
            <ImagePlus size={16} aria-hidden="true" />
            {t("screenshot.add")}
          </button>
          {note !== null && (
            <p id={noteId} className="jp-feedback__note">
              {note}
            </p>
          )}
        </div>
      )}
      <p id={hintId} className="jp-hint">
        {t("screenshot.pasteHint")}
      </p>
    </div>
  );
}
