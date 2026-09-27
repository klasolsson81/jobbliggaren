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

  it("val av ett steg gör en direkt transition, även bakåt", async () => {
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

// #1827 item 10: a move to Accepterad, Nekad or Återtagen deletes the saved copy's text,
// which undo does not bring back, so the list asks first whenever the copy may have text.
describe("StatusMenu — terminal moves ask first (#1827)", () => {
  const BODY =
    "Annonstexten och eventuella kontaktuppgifter i den sparade kopian raderas och kommer inte tillbaka om du ångrar.";
  const FALLBACK =
    "Om ansökan har en sparad kopia av annonsen raderas dess text och eventuella kontaktuppgifter och kommer inte tillbaka om du ångrar.";

  async function choose(
    label: string,
    overrides: Partial<ApplicationDto>,
  ): Promise<ReturnType<typeof userEvent.setup>> {
    const user = userEvent.setup();
    renderMenu(makeApplication(overrides));
    await user.click(screen.getByRole("button", { name: "Byt status" }));
    await user.click(await screen.findByRole("menuitem", { name: label }));
    return user;
  }

  it.each([
    ["Accepterad", "Accepted"],
    ["Nekad", "Rejected"],
    ["Återtagen", "Withdrawn"],
  ] as const)(
    "%s with text in the copy: the dialog states what is deleted, and only its button moves",
    async (label, target) => {
      const user = await choose(label, { hasPreservedAdText: true });

      const dialog = await screen.findByRole("dialog", {
        name: `Markera som ${label}?`,
      });
      expect(dialog).toHaveAccessibleDescription(BODY);
      expect(transitionStatusAction).not.toHaveBeenCalled();

      await user.click(
        within(dialog).getByRole("button", { name: `Markera som ${label}` }),
      );
      await waitFor(() =>
        expect(transitionStatusAction).toHaveBeenCalledWith(
          "11111111-2222-3333-4444-555555555555",
          target,
        ),
      );
    },
  );

  it.each(["Accepterad", "Nekad", "Återtagen"])(
    "%s with the flag missing (deploy skew): asks, with the fallback body",
    async (label) => {
      await choose(label, { hasPreservedAdText: undefined });

      expect(
        await screen.findByRole("dialog", { name: `Markera som ${label}?` }),
      ).toHaveAccessibleDescription(FALLBACK);
      expect(transitionStatusAction).not.toHaveBeenCalled();
    },
  );

  it.each([
    ["Accepterad", "Accepted"],
    ["Nekad", "Rejected"],
    ["Återtagen", "Withdrawn"],
  ] as const)(
    "%s with no text in the copy: moves at once, nothing to confirm",
    async (label, target) => {
      await choose(label, { hasPreservedAdText: false });

      await waitFor(() =>
        expect(transitionStatusAction).toHaveBeenCalledWith(
          "11111111-2222-3333-4444-555555555555",
          target,
        ),
      );
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    },
  );

  it("Inget svar never asks: Ghosted keeps the copy", async () => {
    await choose("Inget svar", { hasPreservedAdText: true });

    await waitFor(() =>
      expect(transitionStatusAction).toHaveBeenCalledWith(
        "11111111-2222-3333-4444-555555555555",
        "Ghosted",
      ),
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("Avbryt moves nothing and returns focus to the menu's trigger", async () => {
    const user = await choose("Nekad", { hasPreservedAdText: true });
    const dialog = await screen.findByRole("dialog", {
      name: "Markera som Nekad?",
    });

    await user.click(within(dialog).getByRole("button", { name: "Avbryt" }));

    await waitFor(() =>
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
    );
    expect(transitionStatusAction).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Byt status" })).toHaveFocus(),
    );
  });
});
