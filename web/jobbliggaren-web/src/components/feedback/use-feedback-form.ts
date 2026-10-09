import { useRef, useState } from "react";
import {
  feedbackSubmitOutcomeSchema,
  type FeedbackSubmissionPayload,
  type FeedbackSubmitOutcome,
} from "@/lib/dto/feedback";
import { readClientContext } from "@/lib/feedback/client-context";
import {
  prepareScreenshot,
  type DecodedImage,
  type PreparedScreenshot,
  type ScreenshotCodec,
} from "@/lib/feedback/image/prepare";
import type { FeedbackPageKey } from "@/lib/feedback/page-keys";
import { keyFor, type FeedbackContent, type KeyedContent } from "@/lib/feedback/submission-key";

export type ScreenshotRefusal = Extract<PreparedScreenshot, { readonly kind: "refused" }>["reason"];

export type ScreenshotState =
  | { readonly kind: "none" }
  | { readonly kind: "preparing"; readonly job: number }
  | { readonly kind: "ready"; readonly blob: Blob; readonly width: number; readonly height: number }
  | { readonly kind: "refused"; readonly reason: ScreenshotRefusal };

export type SendRefusal = Exclude<FeedbackSubmitOutcome, { readonly outcome: "saved" }>;

export type SendPhase =
  | { readonly kind: "idle" }
  | { readonly kind: "sending" }
  | { readonly kind: "refused"; readonly refusal: SendRefusal }
  | { readonly kind: "saved" };

/** The latest thing the form's live region says. */
export type FeedbackAnnouncement =
  | { readonly kind: "sending" }
  | { readonly kind: "preparing" }
  | { readonly kind: "screenshot"; readonly reason: ScreenshotRefusal }
  | { readonly kind: "refused"; readonly refusal: SendRefusal };

export type FeedbackFormState = {
  readonly rating: number | null;
  readonly comment: string;
  readonly screenshot: ScreenshotState;
  readonly shareDeviceContext: boolean;
  /** The key the last send used and what it carried, kept until a send is saved. */
  readonly keyed: KeyedContent | null;
  readonly phase: SendPhase;
  readonly announcement: FeedbackAnnouncement | null;
};

export type FeedbackFormController = {
  readonly state: FeedbackFormState;
  /** Something the user has entered or changed, which closing the surface would throw away. */
  readonly hasDraft: boolean;
  readonly setRating: (rating: number | null) => void;
  readonly setComment: (comment: string) => void;
  readonly setShareDeviceContext: (share: boolean) => void;
  readonly addScreenshot: (file: Blob) => void;
  readonly removeScreenshot: () => void;
  readonly send: () => void;
  /** Sends the rating alone, under the same key rules as `send`: the row's one-press answer. */
  readonly sendRating: (rating: number) => void;
  readonly reset: () => void;
};

const IDLE: SendPhase = { kind: "idle" };
const UNKNOWN: FeedbackSubmitOutcome = { outcome: "unknown" };

const INITIAL: FeedbackFormState = {
  rating: null,
  comment: "",
  screenshot: { kind: "none" },
  shareDeviceContext: false,
  keyed: null,
  phase: IDLE,
  announcement: null,
};

/**
 * An edit makes the last send's refusal stale: it described content that is no longer what Send
 * would carry. "Skicka igen" in particular promises one saved record, which holds only for the
 * content the lost answer belonged to.
 */
function edited(
  previous: FeedbackFormState,
  change: Partial<FeedbackFormState>,
): FeedbackFormState {
  if (previous.phase.kind !== "refused") return { ...previous, ...change };
  const announcement = previous.announcement?.kind === "refused" ? null : previous.announcement;
  return { ...previous, announcement, ...change, phase: IDLE };
}

async function prepare<T extends DecodedImage>(file: Blob, codec: ScreenshotCodec<T>): Promise<PreparedScreenshot> {
  try {
    return await prepareScreenshot(file, codec);
  } catch {
    return { kind: "refused", reason: "unreadable" };
  }
}

/**
 * One feedback form's state and its Send (#1979 PR3, ADR 0156 D2). The owner holds it rather than the
 * form, so a dialog keeps its draft over close and reopen, and an answer that lands after the form
 * has gone still reaches the owner.
 *
 * The device context is read at Send and only when the box is ticked. Nothing is written to browser
 * storage: a draft lives as long as the component that owns it.
 */
export function useFeedbackForm<T extends DecodedImage>({
  page,
  renderedVersion,
  codec,
  onSaved,
  initialRating = null,
}: {
  page: FeedbackPageKey;
  renderedVersion: string | null;
  codec: ScreenshotCodec<T>;
  onSaved: () => void;
  /** The rating the form starts with; a reset starts empty. */
  initialRating?: number | null;
}): FeedbackFormController {
  const [state, setState] = useState<FeedbackFormState>(() => ({ ...INITIAL, rating: initialRating }));
  // A second press can arrive before the render that shows the first one as sending.
  const sendingRef = useRef(false);
  const jobRef = useRef(0);

  async function post(key: string, content: FeedbackContent): Promise<FeedbackSubmitOutcome> {
    const comment = content.comment.trim();
    const payload: FeedbackSubmissionPayload = {
      submissionKey: key,
      page: content.page,
      ...(content.rating !== null ? { rating: content.rating } : {}),
      ...(comment !== "" ? { comment } : {}),
      ...(content.shareDeviceContext ? { client: readClientContext(window) } : {}),
      // The route decides whether the device context goes on; the form always says what it was rendered by.
      ...(renderedVersion !== null ? { renderedVersion } : {}),
    };
    const body = new FormData();
    body.append("payload", JSON.stringify(payload));
    if (content.screenshot !== null) body.append("screenshot", content.screenshot, "screenshot.png");
    try {
      const response = await fetch("/api/feedback", { method: "POST", body, cache: "no-store" });
      const parsed = feedbackSubmitOutcomeSchema.safeParse(await response.json());
      return parsed.success ? parsed.data : UNKNOWN;
    } catch {
      return UNKNOWN;
    }
  }

  function submit(content: FeedbackContent) {
    const keyed = keyFor(state.keyed, content);
    sendingRef.current = true;
    setState((previous) => ({
      ...previous,
      rating: content.rating,
      keyed,
      phase: { kind: "sending" },
      announcement: { kind: "sending" },
    }));
    void post(keyed.key, content).then((outcome) => {
      sendingRef.current = false;
      if (outcome.outcome === "saved") {
        setState((previous) => ({ ...previous, keyed: null, phase: { kind: "saved" }, announcement: null }));
        onSaved();
        return;
      }
      setState((previous) => ({
        ...previous,
        phase: { kind: "refused", refusal: outcome },
        announcement: { kind: "refused", refusal: outcome },
      }));
    });
  }

  function send() {
    if (sendingRef.current || state.phase.kind === "sending" || state.screenshot.kind === "preparing") return;
    if (state.rating === null && state.comment.trim() === "") {
      // An image alone is not feedback: the rating or the text is what is answered.
      const refusal: SendRefusal = { outcome: "refused", reason: "empty" };
      setState((previous) => ({ ...previous, phase: { kind: "refused", refusal }, announcement: { kind: "refused", refusal } }));
      return;
    }
    submit({
      page,
      rating: state.rating,
      comment: state.comment,
      screenshot: state.screenshot.kind === "ready" ? state.screenshot.blob : null,
      shareDeviceContext: state.shareDeviceContext,
    });
  }

  function sendRating(rating: number) {
    if (sendingRef.current || state.phase.kind === "sending") return;
    submit({ page, rating, comment: "", screenshot: null, shareDeviceContext: false });
  }

  function addScreenshot(file: Blob) {
    const job = ++jobRef.current;
    setState((previous) => ({
      ...edited(previous, { screenshot: { kind: "preparing", job } }),
      announcement: { kind: "preparing" },
    }));
    void prepare(file, codec).then((prepared) => {
      setState((previous) => {
        // A newer pick, a removal or a reset has replaced this one.
        if (previous.screenshot.kind !== "preparing" || previous.screenshot.job !== job) return previous;
        return prepared.kind === "ready"
          ? {
              ...previous,
              screenshot: { kind: "ready", blob: prepared.blob, width: prepared.width, height: prepared.height },
              announcement: null,
            }
          : {
              ...previous,
              screenshot: { kind: "refused", reason: prepared.reason },
              announcement: { kind: "screenshot", reason: prepared.reason },
            };
      });
    });
  }

  return {
    state,
    hasDraft:
      state.rating !== null ||
      state.comment !== "" ||
      state.screenshot.kind !== "none" ||
      state.shareDeviceContext,
    setRating: (rating) => setState((previous) => edited(previous, { rating })),
    setComment: (comment) => setState((previous) => edited(previous, { comment })),
    setShareDeviceContext: (shareDeviceContext) => setState((previous) => edited(previous, { shareDeviceContext })),
    addScreenshot,
    removeScreenshot: () => {
      jobRef.current++;
      setState((previous) => {
        const next = edited(previous, { screenshot: { kind: "none" } });
        const aboutImage = next.announcement?.kind === "preparing" || next.announcement?.kind === "screenshot";
        return aboutImage ? { ...next, announcement: null } : next;
      });
    },
    send,
    sendRating,
    reset: () => {
      jobRef.current++;
      setState(INITIAL);
    },
  };
}
