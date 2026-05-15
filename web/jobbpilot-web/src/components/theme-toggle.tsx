"use client";

import { Moon, Sun } from "lucide-react";
import { useTheme } from "@/components/theme-provider";

/**
 * Delad tema-toggle (light/dark). Används av både app-skalet och
 * landningssidan — en källa, ingen drift i aria-label/storlek.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { theme, setTheme } = useTheme();
  const isDark = theme === "dark";
  return (
    <button
      type="button"
      className={className ?? "jp-iconbtn"}
      aria-label={isDark ? "Byt till ljust läge" : "Byt till mörkt läge"}
      title={isDark ? "Ljust läge" : "Mörkt läge"}
      onClick={() => setTheme(isDark ? "light" : "dark")}
    >
      {isDark ? <Sun size={15} /> : <Moon size={15} />}
    </button>
  );
}
