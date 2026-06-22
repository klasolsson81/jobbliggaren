"use client";

import { Moon, Sun } from "lucide-react";
import { useTranslations } from "next-intl";
import { useTheme } from "@/components/theme-provider";

/**
 * Delad tema-toggle (light/dark). Används av både app-skalet och
 * landningssidan — en källa, ingen drift i aria-label/storlek.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const t = useTranslations("common");
  const { theme, setTheme } = useTheme();
  const isDark = theme === "dark";
  return (
    <button
      type="button"
      className={className ?? "jp-iconbtn"}
      aria-label={isDark ? t("themeToggle.toLight") : t("themeToggle.toDark")}
      title={isDark ? t("themeToggle.light") : t("themeToggle.dark")}
      onClick={() => setTheme(isDark ? "light" : "dark")}
    >
      {isDark ? <Sun size={15} /> : <Moon size={15} />}
    </button>
  );
}
