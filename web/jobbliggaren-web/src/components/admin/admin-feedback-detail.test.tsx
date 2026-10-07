import { Component, useState, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminFeedbackRefusal } from "@/lib/admin/feedback";
import type { AdminFeedbackItem, AdminFeedbackNoticeState, AdminFeedbackStatus } from "@/lib/admin/view-models";
import { dismissAdminToast, getAdminToastSnapshot } from "@/lib/admin/toast-store";
import { AdminFeedbackDetail } from "./admin-feedback-detail";

class Boundary extends Component<{ readonly children: ReactNode }, { readonly failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  render() {
    return this.state.failed ? <p>Något gick fel.</p> : this.props.children;
  }
}

const ITEM: AdminFeedbackItem = {
  id: "00000000-0000-4000-8000-000000000501",
  page: "applications",
  rating: 2,
  comment: "När jag sparar en ansökan visas den gamla statusen.",
  status: "new",
  submittedAt: "2026-10-04T05:12:00Z",
  statusChangedAt: null,
  reporterEmail: "konto.b@example.test",
  client: {
    viewportWidth: 1440,
    viewportHeight: 789,
    screenWidth: 1440,
    screenHeight: 900,
    pixelRatio: 1,
    theme: "light",
    deviceClass: "desktop",
    os: "windows",
    browser: "firefox",
  },
  appVersion: "4f2a91c",
  notice: { state: "accepted", attempts: 1, nextAttemptAt: "2026-10-04T05:12:00Z" },
};

type Commands = {
  readonly onStatus: (id: string, status: AdminFeedbackStatus) => Promise<AdminFeedbackRefusal>;
  readonly onRequeue: (id: string, acknowledge: boolean) => Promise<AdminFeedbackRefusal>;
};

const accepted = async () => null;

/**
 * The caller the live page is: a command that went through is followed by the page's revalidation, which
 * hands the detail the submission as the backend now holds it.
 */
function Revalidating({ item, onStatus = accepted, onRequeue = accepted }: { readonly item: AdminFeedbackItem } & Partial<Commands>) {
  const [current, setCurrent] = useState(item);
  return (
    <AdminFeedbackDetail
      region={{ kind: "loaded", data: current }}
      onStatus={async (id, status) => {
        const outcome = await onStatus(id, status);
        if (outcome === null) setCurrent((previous) => ({ ...previous, status, statusChangedAt: "2026-10-05T08:00:00Z" }));
        return outcome;
      }}
      onRequeue={async (id, acknowledge) => {
        const outcome = await onRequeue(id, acknowledge);
        if (outcome === null) setCurrent((previous) => ({
            ...previous,
            notice: { state: "queued", attempts: 0, nextAttemptAt: "2026-10-05T08:00:00Z" },
          }));
        return outcome;
      }}
    />
  );
}

const withNotice = (state: AdminFeedbackNoticeState, attempts = 1): AdminFeedbackItem => ({
  ...ITEM,
  notice: { state, attempts, nextAttemptAt: "2026-10-04T05:20:00Z" },
});

const detail = () => screen.getByRole("region", { name: "Valt inskick" });

afterEach(() => {
  vi.restoreAllMocks();
  const toast = getAdminToastSnapshot();
  if (toast !== null) dismissAdminToast(toast.token);
});

describe("AdminFeedbackDetail — the status (#1979)", () => {
  it("saves the chosen status when it is pressed, names the save while it runs, and confirms it with a receipt", async () => {
    let settle: (outcome: AdminFeedbackRefusal) => void = () => {};
    const onStatus = vi.fn(() => new Promise<AdminFeedbackRefusal>((resolve) => (settle = resolve)));
    render(<Revalidating item={ITEM} onStatus={onStatus} />);

    const select = within(detail()).getByRole("combobox", { name: "Status" });
    expect(within(select).getAllByRole("option").map((option) => option.textContent)).toEqual([
      "Ny",
      "Pågår",
      "Åtgärdad",
      "Avstår",
    ]);
    await userEvent.selectOptions(select, "Pågår");
    expect(onStatus).not.toHaveBeenCalled();

    await userEvent.click(within(detail()).getByRole("button", { name: "Spara status" }));
    expect(onStatus).toHaveBeenCalledWith(ITEM.id, "inProgress");
    const saving = within(detail()).getByRole("button", { name: "Sparar…" });
    expect(saving).toHaveAttribute("aria-disabled", "true");
    expect(saving).toHaveFocus();

    settle(null);
    expect(await within(detail()).findByRole("button", { name: "Spara status" })).toHaveFocus();
    expect(getAdminToastSnapshot()?.message).toBe("Statusen är ändrad till Pågår.");
    expect(within(detail()).getByText("Pågår", { selector: ".jp-pill" })).toBeInTheDocument();
    expect(select).toHaveValue("inProgress");
  });

  it("refuses a status the submission already has, without asking the backend", async () => {
    const onStatus = vi.fn(accepted);
    render(<Revalidating item={ITEM} onStatus={onStatus} />);

    await userEvent.click(within(detail()).getByRole("button", { name: "Spara status" }));

    const refusal = within(detail()).getByRole("alert");
    expect(refusal).toHaveTextContent("Inskicket har redan den statusen.");
    await waitFor(() => expect(refusal).toHaveFocus());
    expect(onStatus).not.toHaveBeenCalled();
  });

  it("shows a refusal where the status was asked, takes focus to it, and leaves the status as it was", async () => {
    render(<Revalidating item={ITEM} onStatus={async () => "Statusen sparades inte. Ladda om sidan och försök igen."} />);

    await userEvent.selectOptions(within(detail()).getByRole("combobox", { name: "Status" }), "Avstår");
    await userEvent.click(within(detail()).getByRole("button", { name: "Spara status" }));

    const refusal = await within(detail()).findByRole("alert");
    expect(refusal).toHaveTextContent("Statusen sparades inte. Ladda om sidan och försök igen.");
    await waitFor(() => expect(refusal).toHaveFocus());
    expect(within(detail()).getByText("Ny", { selector: ".jp-pill" })).toBeInTheDocument();
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("hands a command that throws to the nearest error boundary instead of holding the form", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    render(
      <Boundary>
        <Revalidating item={ITEM} onStatus={async () => Promise.reject(new Error("network"))} />
      </Boundary>,
    );

    await userEvent.selectOptions(within(detail()).getByRole("combobox", { name: "Status" }), "Pågår");
    await userEvent.click(within(detail()).getByRole("button", { name: "Spara status" }));
    expect(await screen.findByText("Något gick fel.")).toBeInTheDocument();
    expect(getAdminToastSnapshot()).toBeNull();
  });
});

describe("AdminFeedbackDetail — the notice (#1979)", () => {
  const notice = () => within(detail()).getByRole("heading", { level: 3, name: "Avisering" });
  const fact = (term: string) => within(detail()).getByText(term, { selector: "dt" }).nextElementSibling;

  it.each([
    ["queued", "Köad", "2026-10-04 07:20"],
    ["sending", "Skickas", null],
    ["accepted", "Mottagen av e-posttjänsten", null],
  ] as const)("names the %s state with its attempts, and offers nothing to do", (state, words, nextAttempt) => {
    render(<Revalidating item={withNotice(state, 2)} />);

    expect(fact("Läge")).toHaveTextContent(words);
    expect(fact("Försök")).toHaveTextContent("2");
    // The next attempt says something only while the notice waits for it.
    if (nextAttempt === null) expect(within(detail()).queryByText("Nästa försök")).toBeNull();
    else expect(fact("Nästa försök")).toHaveTextContent(nextAttempt);
    expect(within(detail()).queryByRole("button", { name: /Försök igen|Skicka avisering igen/ })).toBeNull();
  });

  it("sends a failed notice again at a press, with no question, and lands on the notice", async () => {
    const onRequeue = vi.fn(accepted);
    render(<Revalidating item={withNotice("failed", 5)} onRequeue={onRequeue} />);

    expect(fact("Läge")).toHaveTextContent("Misslyckades");
    expect(fact("Försök")).toHaveTextContent("5");
    await userEvent.click(within(detail()).getByRole("button", { name: "Försök igen" }));

    expect(onRequeue).toHaveBeenCalledWith(ITEM.id, false);
    expect(screen.queryByRole("alertdialog")).toBeNull();
    await waitFor(() => expect(notice()).toHaveFocus());
    expect(getAdminToastSnapshot()?.message).toBe("Aviseringen är köad igen.");
    expect(fact("Läge")).toHaveTextContent("Köad");
    expect(within(detail()).queryByRole("button", { name: "Försök igen" })).toBeNull();
  });

  it("shows a refused requeue where it was asked and takes focus to it", async () => {
    render(<Revalidating item={withNotice("failed", 5)} onRequeue={async () => "Aviseringen är redan köad eller skickad."} />);

    await userEvent.click(within(detail()).getByRole("button", { name: "Försök igen" }));

    const refusal = await within(detail()).findByRole("alert");
    expect(refusal).toHaveTextContent("Aviseringen är redan köad eller skickad.");
    await waitFor(() => expect(refusal).toHaveFocus());
  });

  it("asks before sending a notice whose outcome is unknown again, and returns to the button when the question is cancelled", async () => {
    const onRequeue = vi.fn(accepted);
    render(<Revalidating item={withNotice("unknown")} onRequeue={onRequeue} />);

    expect(fact("Läge")).toHaveTextContent("Utfall okänt");
    const resend = within(detail()).getByRole("button", { name: "Skicka avisering igen" });
    await userEvent.click(resend);

    const question = screen.getByRole("alertdialog", {
      name: "Skicka aviseringen igen?",
      description: "Aviseringen kan redan ha kommit fram. Ett nytt utskick kan ge en dubblett.",
    });
    await waitFor(() => expect(within(question).getByRole("button", { name: "Avbryt" })).toHaveFocus());
    await userEvent.click(within(question).getByRole("button", { name: "Avbryt" }));

    expect(onRequeue).not.toHaveBeenCalled();
    await waitFor(() => expect(resend).toHaveFocus());
  });

  it("sends it again only with the risk of a duplicate acknowledged, and lands on the notice", async () => {
    const onRequeue = vi.fn(accepted);
    render(<Revalidating item={withNotice("unknown")} onRequeue={onRequeue} />);

    await userEvent.click(within(detail()).getByRole("button", { name: "Skicka avisering igen" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Skicka avisering igen" }));

    expect(onRequeue).toHaveBeenCalledWith(ITEM.id, true);
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull());
    expect(getAdminToastSnapshot()?.message).toBe("Aviseringen är köad igen.");
    await waitFor(() => expect(notice()).toHaveFocus());
  });

  it("keeps a refused requeue in the question, which stays open", async () => {
    render(<Revalidating item={withNotice("unknown")} onRequeue={async () => "Aviseringens läge har ändrats. Kontrollera det innan du skickar den igen."} />);

    await userEvent.click(within(detail()).getByRole("button", { name: "Skicka avisering igen" }));
    await userEvent.click(within(screen.getByRole("alertdialog")).getByRole("button", { name: "Skicka avisering igen" }));

    expect(await within(screen.getByRole("alertdialog")).findByRole("alert")).toHaveTextContent(
      "Aviseringens läge har ändrats. Kontrollera det innan du skickar den igen.",
    );
    expect(getAdminToastSnapshot()).toBeNull();
  });
});

describe("AdminFeedbackDetail — focus when it opens (#1979)", () => {
  it("takes focus when it is opened from the list, and leaves it alone when the page's URL opened it", () => {
    const { unmount } = render(<AdminFeedbackDetail region={{ kind: "loaded", data: ITEM }} onStatus={accepted} onRequeue={accepted} />);
    expect(detail()).not.toHaveFocus();
    unmount();

    // The list's link keeps focus through the navigation that opens its submission.
    render(
      <>
        <ol className="jp-adminfeedback__list">
          <li>
            <a href={`/admin/feedback?id=${ITEM.id}`}>Ansökningar</a>
          </li>
        </ol>
        <Opening />
      </>,
    );
    screen.getByRole("link", { name: "Ansökningar" }).focus();
    // A click event alone, which moves no focus: the link keeps it, as it does through a navigation.
    fireEvent.click(screen.getByRole("button", { name: "Öppna" }));
    expect(detail()).toHaveFocus();
  });
});

/** Mounts the detail on demand, as a navigation does, without moving focus itself. */
function Opening() {
  const [open, setOpen] = useState(false);
  return open ? (
    <AdminFeedbackDetail region={{ kind: "loaded", data: ITEM }} onStatus={accepted} onRequeue={accepted} />
  ) : (
    <button type="button" onClick={() => setOpen(true)}>
      Öppna
    </button>
  );
}
