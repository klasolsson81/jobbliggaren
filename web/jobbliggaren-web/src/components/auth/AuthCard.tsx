"use client";

import { useId, useRef, useState, type KeyboardEvent } from "react";
import { useTranslations } from "next-intl";
import { LoginForm } from "@/components/forms/LoginForm";
import { RegisterForm } from "@/components/forms/RegisterForm";

type AuthTab = "register" | "login";

/**
 * On-page auth-kort med flikar (LP-6 / #260). Handrullad APG-tablist (repot har
 * ingen shadcn Tabs) som återanvänder befintliga LoginForm/RegisterForm
 * OFÖRÄNDRADE — de läser fortsatt `pages.auth.*`. AuthCard levererar bara
 * flik-/fine-print-chrome från `landing.auth.*`. Ingen OAuth, inget Namn-fält,
 * ingen placeholder (epic #267). next-param bevaras av formulären själva (de
 * läser en dold `next` via `useSearchParams`); AuthCard rör den aldrig.
 *
 * DECISION (Klas 2026-06-27): registrering är ÖPPEN — båda flikarna är live och
 * default-fliken är "Skapa konto" (live RegisterForm), ingen closed-beta-panel.
 *
 * Aktivering: AUTOMATIC (markering följer fokus) per APG-rekommendationen när
 * panelen visas utan märkbar latens — auth-formulären renderas direkt. Enter/
 * Space aktiverar ändå via native `<button>`-semantik (onClick). Endast den
 * aktiva flikens formulär monteras: LoginForm/RegisterForm delar fasta
 * `id`-attribut (`email`/`password`/...), så att rendera båda samtidigt skulle
 * ge ogiltiga dubblett-id:n. Panel-containrarna finns alltid (så `aria-controls`
 * alltid pekar på ett existerande element); bara formuläret är villkorat.
 */
export function AuthCard() {
  const t = useTranslations("landing.auth");
  const [active, setActive] = useState<AuthTab>("register");
  const baseId = useId();

  const tabRefs = useRef<Record<AuthTab, HTMLButtonElement | null>>({
    register: null,
    login: null,
  });

  const tabs: { value: AuthTab; label: string }[] = [
    { value: "register", label: t("tabRegister") },
    { value: "login", label: t("tabLogin") },
  ];

  const tabId = (value: AuthTab) => `${baseId}-tab-${value}`;
  const panelId = (value: AuthTab) => `${baseId}-panel-${value}`;

  function activate(value: AuthTab) {
    setActive(value);
    tabRefs.current[value]?.focus();
  }

  function onTabKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const last = tabs.length - 1;
    let nextIndex: number;
    switch (event.key) {
      case "ArrowRight":
        nextIndex = index === last ? 0 : index + 1;
        break;
      case "ArrowLeft":
        nextIndex = index === 0 ? last : index - 1;
        break;
      case "Home":
        nextIndex = 0;
        break;
      case "End":
        nextIndex = last;
        break;
      default:
        return;
    }
    event.preventDefault();
    const target = tabs[nextIndex];
    if (target) activate(target.value);
  }

  return (
    <div className="jp-auth-tabcard">
      <div role="tablist" aria-label={t("tablistLabel")} className="jp-auth-tabs">
        {tabs.map((tab, index) => {
          const isActive = tab.value === active;
          return (
            <button
              key={tab.value}
              ref={(element) => {
                tabRefs.current[tab.value] = element;
              }}
              type="button"
              role="tab"
              id={tabId(tab.value)}
              aria-selected={isActive}
              aria-controls={panelId(tab.value)}
              tabIndex={isActive ? 0 : -1}
              className="jp-auth-tab"
              onClick={() => setActive(tab.value)}
              onKeyDown={(event) => onTabKeyDown(event, index)}
            >
              {tab.label}
            </button>
          );
        })}
      </div>

      <div
        role="tabpanel"
        id={panelId("register")}
        aria-labelledby={tabId("register")}
        tabIndex={0}
        hidden={active !== "register"}
        className="jp-auth-panel"
      >
        {active === "register" && (
          <>
            <RegisterForm />
            <p className="jp-auth-fine">{t("fine")}</p>
          </>
        )}
      </div>

      <div
        role="tabpanel"
        id={panelId("login")}
        aria-labelledby={tabId("login")}
        tabIndex={0}
        hidden={active !== "login"}
        className="jp-auth-panel"
      >
        {active === "login" && <LoginForm />}
      </div>
    </div>
  );
}
