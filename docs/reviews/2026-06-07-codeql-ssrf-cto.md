# CTO-dom: åtgärd av 8 CodeQL `js/request-forgery` (SSRF)-alarm

**Agent:** senior-cto-advisor (agentId `a0958041fed7d09ab`)
**Datum:** 2026-06-07
**Beslut:** **Approach D (hybrid)** — B primär (GUID-format/allowlist-validering före request) + A (`encodeURIComponent`) som defense-in-depth, via delad guard-modul. Ingen codeql-config-tuning, ingen dismiss.

## Avgörande tekniska faktum (web-verifierat §9.5)
CodeQL:s query-hjälp för `js/request-forgery` rekommenderar **allowlist/input-restriction**, nämner **inte** `encodeURIComponent` som sanitizer; `encodeURI`/`escape` **togs bort** ur sanitizer-listan i CodeQL 2.22.1. → Approach A (encode) ensam clearar troligen **inte** alarmet → bryter Klas hård-constraint ("varningarna måste försvinna"). B (format-validering) mappar mot query-hjälpens egen remediation → högst sannolik barrier-recognition.

## Motivering (principer)
- **Fail-safe defaults + allowlist > denylist** (Saltzer/Schroeder 1975) — GUID-regex = allowlist.
- **Defense-in-depth** (OWASP ASVS V5) — validering + encoding komplementära lager.
- **DRY** (Hunt/Thomas) — delad `isValidId`/`GUID_REGEX`, ett ställe per knowledge piece.
- **SRP** (Martin 2017) — path-safety-guard skild från DTO-validering.

## Avvisat
- **A ensam:** clearar troligen ej CodeQL + svagare princip.
- **C (dismiss as false-positive):** dismiss-teater; lämnar path-injektion-hygien-gap; återkommande manuellt arbete; 8 critical utan kod-härdning fastnar i Mastercard-granskning.
- **Codeql-config-tuning:** döljer framtida äkta SSRF — behåll queryn skarp.

## Process
- security-auditor-rond **efter** fix, före merge (PII/auth-närhet, §9.2).
- **In-block-fix** (§9.6 — samma fas, FE-kod finns, ingen saknad dependency).
- **CC går direkt till impl** utan extra Klas-GO (§9.6 punkt 5).
- **Verifiering:** re-scan på PR ska flytta alla 8 alarm till Closed/Fixed (ej Dismissed). Kvarstår någon → STOPP. Fallback: CodeQL models-as-data (GitHub Changelog 2026-04-21) deklarerar vår validator, eller riktad dismiss.

## Referenser
CodeQL query help (js/request-forgery), GitHub Changelog 2026-04-21 (models-as-data sanitizers/validators), Saltzer/Schroeder 1975, OWASP ASVS V5, Hunt/Thomas (DRY), Martin 2017 (SRP).
