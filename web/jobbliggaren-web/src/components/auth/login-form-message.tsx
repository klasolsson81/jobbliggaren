import type { Ref } from "react";
import type { MessageChannel } from "@/lib/auth/challenge-action-state";

// The one message slot of a login-flow form. It holds exactly one message, in one of two channels
// (design-reviewer, #1738): position follows arrival mode, and this is the outcome of a submit the
// user just made, so it sits in the form, next to the control that was pressed.
//
// `field`: something the user can correct. `role="alert"`, danger colour, and the FORM moves focus
//   to the input the message is wired to through `aria-describedby`.
// `status`: a throttle or an outage. Nothing to correct, so never an alert and never danger colour,
//   which would read as a rejection of what was typed. Focus moves to the message itself: the
//   pressed button is disabled while the action runs, so focus would otherwise fall to <body>.
export function LoginFormMessage({
  id,
  message,
  channel,
  statusRef,
}: {
  id: string;
  message: string;
  channel: MessageChannel;
  statusRef: Ref<HTMLParagraphElement>;
}) {
  if (channel === "field") {
    return (
      <p id={id} role="alert" className="text-body-sm leading-5 text-danger-600 [overflow-wrap:anywhere]">
        {message}
      </p>
    );
  }
  return (
    <p
      ref={statusRef}
      tabIndex={-1}
      role="status"
      aria-live="polite"
      className="text-body-sm leading-5 text-text-primary [overflow-wrap:anywhere]"
    >
      {message}
    </p>
  );
}
