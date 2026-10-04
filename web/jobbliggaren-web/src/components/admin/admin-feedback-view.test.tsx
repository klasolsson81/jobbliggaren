import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { AdminFeedbackItem } from "@/lib/admin/view-models";
import { dismissAdminToast, getAdminToastSnapshot } from "@/lib/admin/toast-store";
import { AdminFeedbackView } from "./admin-feedback-view";

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
const detail = () => screen.getByRole("region", { name: "Vald rapport" });

afterEach(() => {
  const toast = getAdminToastSnapshot();
  if (toast !== null) dismissAdminToast(toast.token);
});

describe("AdminFeedbackView with reports (ADR 0150)", () => {
  it("counts each filter, lists the reports and opens the first", () => {
    render(<AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} />);

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

  it("opens the report pressed in the list, and narrows the list by status", async () => {
    render(<AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} />);

    await userEvent.click(within(list()).getByRole("button", { name: /Hur länge sparas/ }));
    expect(within(list()).getByRole("button", { name: /Hur länge sparas/ })).toHaveAttribute("aria-current", "true");
    expect(within(detail()).getByText("I tolv månader.")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("radio", { name: "Nya (1)" }));
    expect(within(list()).getAllByRole("button")).toHaveLength(1);
    await userEvent.click(screen.getByRole("radio", { name: "Pågår (0)" }));
    expect(list()).toHaveTextContent("Inga rapporter.");
  });

  it("sends a reply only when there is one, and confirms it with a receipt", async () => {
    const onReply = vi.fn(async () => null);
    render(<AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} onReply={onReply} />);

    const send = within(detail()).getByRole("button", { name: "Skicka svar" });
    expect(send).toBeDisabled();
    expect(send).toHaveClass("jp-btn--primary");
    const field = within(detail()).getByRole("textbox", { name: "Svar" });
    expect(field).toHaveAccessibleDescription("Svaret skickas till konto.b@example.test.");

    await userEvent.type(field, "Tack, vi tittar på det.");
    await userEvent.click(send);
    expect(onReply).toHaveBeenCalledWith(ITEMS[0], "Tack, vi tittar på det.");
    expect(field).toHaveValue("");
    expect(getAdminToastSnapshot()?.message).toBe("Svaret skickades till konto.b@example.test.");
  });

  it("keeps the reply and shows a refusal where it was asked", async () => {
    render(
      <AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} onReply={async () => "Svaret kunde inte skickas."} />,
    );

    const field = within(detail()).getByRole("textbox", { name: "Svar" });
    await userEvent.type(field, "Hej");
    await userEvent.click(within(detail()).getByRole("button", { name: "Skicka svar" }));
    expect(within(detail()).getByRole("alert")).toHaveTextContent("Svaret kunde inte skickas.");
    expect(field).toHaveValue("Hej");
    expect(getAdminToastSnapshot()).toBeNull();
  });

  it("changes a report's status where the caller lets it, and only shows it otherwise", async () => {
    const onStatus = vi.fn();
    const { unmount } = render(<AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} onStatus={onStatus} />);
    await userEvent.selectOptions(within(detail()).getByRole("combobox", { name: "Status" }), "Löst");
    expect(onStatus).toHaveBeenCalledWith(ITEMS[0], "resolved");
    unmount();

    render(<AdminFeedbackView region={{ kind: "loaded", data: ITEMS }} />);
    expect(within(detail()).queryByRole("combobox")).toBeNull();
    expect(within(detail()).getByRole("button", { name: "Skicka svar" })).toHaveClass("jp-btn--secondary");
  });

  it.each([
    ["empty", "Inga rapporter."],
    ["failed", "Uppgifterna kunde inte hämtas. Försök igen om en stund."],
    ["loading", "Hämtar uppgifter"],
  ] as const)("in the %s state shows one line and no report", (kind, line) => {
    render(<AdminFeedbackView region={{ kind }} />);

    expect(list()).toHaveTextContent(line);
    expect(screen.queryByRole("region", { name: "Vald rapport" })).toBeNull();
  });
});
