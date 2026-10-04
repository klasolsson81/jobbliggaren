import { Component, useState, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminFeedbackItem, AdminFeedbackStatus } from "@/lib/admin/view-models";
import { dismissAdminToast, getAdminToastSnapshot } from "@/lib/admin/toast-store";
import { AdminFeedbackView } from "./admin-feedback-view";

class Boundary extends Component<{ readonly children: ReactNode }, { readonly failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() {
    return { failed: true };
  }
  render() {
    return this.state.failed ? <p>Något gick fel.</p> : this.props.children;
  }
}

const base = {
  screen: "1440 × 900",
  device: "Firefox 131, Windows",
  version: "0.1.0",
  replies: [],
} as const;

const ITEMS: ReadonlyArray<AdminFeedbackItem> = [
  {
    ...base,
    id: "f1",
    status: "new",
    category: "bug",
    receivedAt: "2026-10-04T05:12:00Z",
    text: "När jag sparar en ansökan och går tillbaka till listan visas den gamla statusen tills jag laddar om sidan.",
    senderEmail: "konto.b@example.test",
    page: "/ansokningar",
  },
  {
    ...base,
    id: "f2",
    status: "resolved",
    category: "question",
    receivedAt: "2026-10-02T09:03:00Z",
    text: "Hur länge sparas mitt CV?",
    senderEmail: "konto.g@example.test",
    page: "/cv/granska",
    replies: [{ id: "r1", sentAt: "2026-10-03T08:00:00Z", text: "I tolv månader." }],
  },
];

const list = () => screen.getByRole("region", { name: "Rapporter" });
const noReply = async () => {};
const noStatus = () => {};
const detail = () => screen.getByRole("region", { name: "Vald rapport" });

/**
 * The caller the preview is: a sent reply joins its report and moves a new report to Pågår, and a saved
 * status replaces the report's.
 */
function Reports({
  onReply = noReply,
  onStatus = noStatus,
}: {
  readonly onReply?: (id: string, text: string) => void | Promise<void>;
  readonly onStatus?: (id: string, status: AdminFeedbackStatus) => void;
}) {
  const [items, setItems] = useState(ITEMS);
  return (
    <AdminFeedbackView
      region={{ kind: "loaded", data: items }}
      onReply={async (id, text) => {
        await onReply(id, text);
        setItems((previous) =>
          previous.map((item) =>
            item.id === id
              ? {
                  ...item,
                  status: item.status === "new" ? "inProgress" : item.status,
                  replies: [...item.replies, { id: `${id}-sent`, sentAt: "2026-10-04T08:00:00Z", text }],
                }
              : item,
          ),
        );
      }}
      onStatus={(id, status) => {
        onStatus(id, status);
        setItems((previous) => previous.map((item) => (item.id === id ? { ...item, status } : item)));
      }}
    />
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  const toast = getAdminToastSnapshot();
  if (toast !== null) dismissAdminToast(toast.token);
});

describe("AdminFeedbackView with reports (ADR 0150)", () => {
  it("counts each filter, lists the reports and opens the first", () => {
    render(<AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} onReply={noReply} onStatus={noStatus} />);

    expect(
      within(screen.getByRole("radiogroup", { name: "Visa rapporter" }))
        .getAllByRole("radio")
        .map((radio) => radio.textContent),
    ).toEqual(["Alla (2)", "Nya (1)", "Pågår (0)", "Lösta (1)", "Avfärdade (0)"]);
    const items = within(list()).getAllByRole("button");
    expect(items[0]).toHaveAttribute("aria-current", "true");
    expect(items[0]).toHaveTextContent(
      "NyFel2026-10-04 07:12När jag sparar en ansökan och går tillbaka till listan visas den gamla statusen tills jag…konto.b@example.test",
    );
    expect(detail()).toHaveTextContent("konto.b@example.test");
    expect(within(detail()).getByText("Sida").nextElementSibling).toHaveTextContent("/ansokningar");
  });

  it("opens the report pressed in the list and moves focus to it, and narrows the list by status", async () => {
    render(<AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} onReply={noReply} onStatus={noStatus} />);

    await userEvent.click(within(list()).getByRole("button", { name: /Hur länge sparas/ }));
    expect(within(list()).getByRole("button", { name: /Hur länge sparas/ })).toHaveAttribute("aria-current", "true");
    expect(within(detail()).getByText("I tolv månader.")).toBeInTheDocument();
    expect(detail()).toHaveFocus();

    await userEvent.click(screen.getByRole("radio", { name: "Nya (1)" }));
    expect(within(list()).getAllByRole("button")).toHaveLength(1);
    await userEvent.click(screen.getByRole("radio", { name: "Pågår (0)" }));
    expect(list()).toHaveTextContent("Inga rapporter.");
  });

  it("sends no empty reply, and confirms a sent one with a receipt and focus on it", async () => {
    const onReply = vi.fn();
    render(<Reports onReply={onReply} />);

    const send = within(detail()).getByRole("button", { name: "Skicka svar" });
    expect(send).toHaveClass("jp-btn--primary");
    const field = within(detail()).getByRole("textbox", { name: "Svar" });
    expect(field).toBeRequired();
    expect(field).toHaveAccessibleDescription("Svaret skickas till konto.b@example.test.");
    await userEvent.click(send);
    expect(onReply).not.toHaveBeenCalled();

    const refused = vi.fn();
    field.addEventListener("invalid", refused);
    await userEvent.type(field, "   ");
    await userEvent.click(send);
    expect(onReply).not.toHaveBeenCalled();
    expect(field).toHaveValue("");
    expect(refused).toHaveBeenCalledTimes(1);

    await userEvent.type(field, "Tack, vi tittar på det.");
    await userEvent.click(send);
    expect(onReply).toHaveBeenCalledWith("f1", "Tack, vi tittar på det.");
    expect(field).toHaveValue("");
    expect(getAdminToastSnapshot()?.message).toBe("Svaret skickades till konto.b@example.test.");
    await waitFor(() => expect(within(detail()).getByText("Tack, vi tittar på det.").closest("li")).toHaveFocus());
  });

  it("keeps a report open and focuses its new reply when the reply moves it out of the filter", async () => {
    render(<Reports />);

    await userEvent.click(screen.getByRole("radio", { name: "Nya (1)" }));
    await userEvent.type(within(detail()).getByRole("textbox", { name: "Svar" }), "Vi har rättat det.");
    await userEvent.click(within(detail()).getByRole("button", { name: "Skicka svar" }));

    await waitFor(() => expect(within(detail()).getByText("Vi har rättat det.").closest("li")).toHaveFocus());
    expect(detail()).toHaveTextContent("konto.b@example.test");
    expect(list()).toHaveTextContent("Inga rapporter.");
  });

  it("names the reply while it is sent", async () => {
    let settle: () => void = () => {};
    render(
      <AdminFeedbackView
        region={{ kind: "loaded", data: ITEMS }}
        onReply={() => new Promise<void>((resolve) => (settle = resolve))}
        onStatus={noStatus}
      />,
    );

    await userEvent.type(within(detail()).getByRole("textbox", { name: "Svar" }), "Hej");
    await userEvent.click(within(detail()).getByRole("button", { name: "Skicka svar" }));
    expect(within(detail()).getByRole("button", { name: "Skickar…" })).toBeDisabled();

    settle();
    expect(await within(detail()).findByRole("button", { name: "Skicka svar" })).toBeEnabled();
  });

  it("hands a send that throws to the nearest error boundary instead of holding the form disabled", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    render(
      <Boundary>
        <AdminFeedbackView
          region={{ kind: "loaded", data: ITEMS }}
          onReply={async () => Promise.reject(new Error("network"))}
          onStatus={noStatus}
        />
      </Boundary>,
    );

    await userEvent.type(within(detail()).getByRole("textbox", { name: "Svar" }), "Hej");
    await userEvent.click(within(detail()).getByRole("button", { name: "Skicka svar" }));
    expect(await screen.findByText("Något gick fel.")).toBeInTheDocument();
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("saves a status only when it is pressed, and keeps the report open when it leaves the filter", async () => {
    const onStatus = vi.fn();
    render(<Reports onStatus={onStatus} />);

    await userEvent.click(screen.getByRole("radio", { name: "Nya (1)" }));
    await userEvent.selectOptions(within(detail()).getByRole("combobox", { name: "Status" }), "Pågår");
    expect(onStatus).not.toHaveBeenCalled();

    const save = within(detail()).getByRole("button", { name: "Spara status" });
    await userEvent.click(save);
    expect(onStatus).toHaveBeenCalledWith("f1", "inProgress");
    expect(list()).toHaveTextContent("Inga rapporter.");
    expect(detail()).toHaveTextContent("konto.b@example.test");
    expect(save).toHaveFocus();
  });

  it.each([
    ["empty", "Inga rapporter."],
    ["failed", "Uppgifterna kunde inte hämtas. Försök igen om en stund."],
    ["loading", "Hämtar uppgifter…"],
  ] as const)("in the %s state shows one line and no report", (kind, line) => {
    render(<AdminFeedbackView region={{ kind }} onReply={noReply} onStatus={noStatus} />);

    expect(list()).toHaveTextContent(line);
    expect(screen.queryByRole("region", { name: "Vald rapport" })).toBeNull();
  });
});
