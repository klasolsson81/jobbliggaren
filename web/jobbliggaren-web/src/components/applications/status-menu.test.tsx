import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApplicationActionsProvider } from "./application-actions";
import { StatusMenu } from "./status-menu";
import type { ApplicationDto } from "@/lib/types/applications";

const transitionStatusAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
const logFollowUpAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
const deleteApplicationAction = vi.hoisted(() =>
  vi.fn(async () => ({ success: true as const })),
);
vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction,
  logFollowUpAction,
  deleteApplicationAction,
}));

function makeApplication(
  overrides: Partial<ApplicationDto> = {},
): ApplicationDto {
  return {
    id: "11111111-2222-3333-4444-555555555555",
    jobSeekerId: "seeker-1",
    jobAdId: null,
    status: "Submitted",
    createdAt: "2026-05-01",
    updatedAt: "2026-05-10",
    jobAd: null,
    ...overrides,
  };
}

function renderMenu(application: ApplicationDto = makeApplication()) {
  return render(
    <ApplicationActionsProvider>
      <StatusMenu application={application} pending={false} />
    </ApplicationActionsProvider>,
  );
}

beforeEach(() => {
  transitionStatusAction.mockClear();
});

describe("StatusMenu (design §5, #630 PR 7)", () => {
  it("öppnar en meny med båda grupperna och ALLA 10 statusar (fria byten, D3)", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Byt status" }));

    const menu = await screen.findByRole("menu");
    expect(screen.getByText("Flytta till · Aktiv väg")).toBeInTheDocument();
    expect(screen.getByText("Avslut & vilande")).toBeInTheDocument();
    // 10 status-byten + 1 destruktiv "Radera ansökan" (#782). De 10 status-
    // posterna bär färgpricken; delete-posten gör det inte (Trash2-ikon).
    expect(menu.querySelectorAll("[data-slot='dropdown-menu-item']")).toHaveLength(
      11,
    );
    expect(menu.querySelectorAll(".jp-statusmenu__dot")).toHaveLength(10);
  });

  it("visar en destruktiv 'Radera ansökan'-post skild från statusbytena (#782)", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Byt status" }));

    const item = await screen.findByRole("menuitem", { name: "Radera ansökan" });
    // Att välja posten ÖPPNAR bekräftelse-dialogen (ADR 0047) — ingen direkt
    // radering, och absolut ingen statustransition.
    await user.click(item);
    expect(
      await screen.findByRole("dialog", { name: "Radera ansökan?" }),
    ).toBeInTheDocument();
    expect(transitionStatusAction).not.toHaveBeenCalled();
    expect(deleteApplicationAction).not.toHaveBeenCalled();
  });

  // #1827 11c: the delete item unmounts with the menu before its dialog closes, so the
  // dialog cannot return focus to the element that opened it.
  it.each(["Avbryt", "Escape"])(
    "focus returns to the menu's trigger when the delete dialog closes (%s)",
    async (close) => {
      const user = userEvent.setup();
      renderMenu();
      const trigger = screen.getByRole("button", { name: "Byt status" });
      await user.click(trigger);
      await user.click(
        await screen.findByRole("menuitem", { name: "Radera ansökan" }),
      );
      const dialog = await screen.findByRole("dialog", {
        name: "Radera ansökan?",
      });

      if (close === "Escape") await user.keyboard("{Escape}");
      else await user.click(within(dialog).getByRole("button", { name: "Avbryt" }));

      await waitFor(() =>
        expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
      );
      await waitFor(() => expect(trigger).toHaveFocus());
    },
  );

  it("markerar nuvarande status med ✓ och gör den ovalbar (self-transition = no-op)", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Byt status" }));

    const menu = await screen.findByRole("menu");
    const current = [...menu.querySelectorAll("[data-slot='dropdown-menu-item']")]
      .find((el) => el.textContent?.includes("Skickad"));
    expect(current).toBeDefined();
    expect(current).toHaveAttribute("data-disabled");
    // ✓-markören förstärks av en sr-only-text för skärmläsare.
    expect(current!.textContent).toContain("nuvarande status");
  });

  it("val i menyn gör en direkt transition (även bakåt/terminal-hopp)", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Byt status" }));

    await user.click(await screen.findByRole("menuitem", { name: "Utkast" }));
    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "11111111-2222-3333-4444-555555555555",
        "Draft",
      ),
    );
  });

  it("varje rad bär en färgprick keyad på status-varianten (WCAG 1.4.1: färg förstärker)", async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole("button", { name: "Byt status" }));
    await screen.findByRole("menu");

    const dots = document.querySelectorAll(".jp-statusmenu__dot");
    expect(dots).toHaveLength(10);
    expect(
      document.querySelectorAll(
        ".jp-statusmenu__dot[data-status-variant='brand']",
      ).length,
    ).toBeGreaterThan(0);
  });
});
