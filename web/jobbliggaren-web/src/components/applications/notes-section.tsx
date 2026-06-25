"use client";

import { useEffect, useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ChevronDown } from "lucide-react";
import { AddNoteForm } from "./add-note-form";
import { formatDate } from "@/lib/i18n/format";
import type { NoteDto } from "@/lib/types/applications";

interface NotesSectionProps {
  applicationId: string;
  notes: ReadonlyArray<NoteDto>;
}

const SECTION_LABEL_STYLE: React.CSSProperties = {
  fontSize: 13,
  fontWeight: 700,
  textTransform: "uppercase",
  letterSpacing: "0.06em",
  color: "var(--jp-ink-2)",
  marginBottom: 10,
};

/**
 * Disclosure-sektion för anteckningar (Klas pre-F6 Prompt 4 2026-05-20).
 *
 * Mönster (speglar follow-ups-section):
 *  - Kompakt rad per anteckning: datum + första raden. Klick expanderar
 *    till full text.
 *  - Endast EN rad expanderad åt gången.
 *  - "Lägg till anteckning" är en knapp som default; klick → form expanderar
 *    inline. Lyckad spar eller Avbryt → kollapsa.
 *  - Esc kollapsar aktiv editor / aktiv expanderad rad.
 */
export function NotesSection({ applicationId, notes }: NotesSectionProps) {
  const tUi = useTranslations("applications.ui");
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        setExpandedId(null);
        setAddOpen(false);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const sorted = [...notes].sort(
    (a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  );

  return (
    <div>
      <div style={SECTION_LABEL_STYLE}>{tUi("notes.sectionLabel")}</div>

      {sorted.length === 0 ? (
        <p className="text-body-sm text-text-secondary">
          {tUi("notes.empty")}
        </p>
      ) : (
        <ul className="flex flex-col gap-2" role="list">
          {sorted.map((note) => (
            <NoteRow
              key={note.id}
              note={note}
              expanded={expandedId === note.id}
              onToggle={() =>
                setExpandedId((prev) =>
                  prev === note.id ? null : note.id,
                )
              }
            />
          ))}
        </ul>
      )}

      <div className="mt-4">
        {!addOpen ? (
          <button
            type="button"
            className="jp-btn jp-btn--secondary"
            onClick={() => setAddOpen(true)}
          >
            {tUi("notes.add")}
          </button>
        ) : (
          <div className="jp-disclosure-body">
            <h3 className="mb-3 text-body font-medium text-text-primary">
              {tUi("notes.addHeading")}
            </h3>
            <AddNoteForm
              applicationId={applicationId}
              onSuccess={() => setAddOpen(false)}
              onCancel={() => setAddOpen(false)}
            />
          </div>
        )}
      </div>
    </div>
  );
}

interface NoteRowProps {
  note: NoteDto;
  expanded: boolean;
  onToggle: () => void;
}

function NoteRow({ note, expanded, onToggle }: NoteRowProps) {
  const format = useFormatter();
  const createdAtLabel =
    formatDate(format, note.createdAt) ?? "";
  const firstLine = note.content?.split(/\r?\n/)[0] ?? "";

  return (
    <li>
      <button
        type="button"
        className="jp-disclosure-row"
        aria-expanded={expanded}
        onClick={onToggle}
      >
        <span className="jp-disclosure-row__note">{firstLine}</span>
        <span className="jp-disclosure-row__date jp-mono">
          {createdAtLabel}
        </span>
        <ChevronDown
          size={16}
          className="jp-disclosure-row__chevron"
          style={{
            transform: expanded ? "rotate(180deg)" : "rotate(0deg)",
            transition: "transform 120ms ease",
          }}
          aria-hidden="true"
        />
      </button>

      {expanded && (
        <div className="jp-disclosure-body">
          <p className="text-body text-text-primary whitespace-pre-line">
            {note.content ?? ""}
          </p>
        </div>
      )}
    </li>
  );
}
