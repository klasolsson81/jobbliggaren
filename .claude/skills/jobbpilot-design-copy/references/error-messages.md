# JobbPilot — Backend Error Codes & Swedish Translations

All user-facing error messages in Swedish. Backend returns error codes;
frontend maps to these strings. Never show raw error codes, HTTP statuses,
or stack traces to users.

---

## Mapping convention

Backend returns `{ "code": "AUTH_INVALID_CREDENTIALS", "requestId": "abc123" }`.
Frontend looks up the code in this table. If code is unknown → fall back to
the Unknown Error pattern at the bottom of this file.

---

## Authentication (AUTH_*)

| Code | Meddelande | Kommentar |
|---|---|---|
| `AUTH_INVALID_CREDENTIALS` | "Inloggningen misslyckades. Kontrollera e-post och lösenord." | Aldrig specificera vilket fält som är fel (security) |
| `AUTH_EMAIL_NOT_VERIFIED` | "Verifiera din e-postadress innan du loggar in. Kolla inkorgen." | — |
| `AUTH_ACCOUNT_LOCKED` | "Kontot är tillfälligt låst efter för många misslyckade inloggningsförsök. Försök igen om 15 minuter." | — |
| `AUTH_SESSION_EXPIRED` | "Din session har gått ut. Logga in igen." | — |
| `AUTH_TOKEN_INVALID` | "Länken är inte giltig eller har gått ut. Begär en ny länk." | Reset-links, verify-links |
| `AUTH_OAUTH_FAILED` | "Inloggning via Google misslyckades. Försök igen eller logga in med e-post." | — |

---

## Validering (VALIDATION_*)

| Code | Meddelande | Kommentar |
|---|---|---|
| `VALIDATION_EMAIL_FORMAT` | "E-postadressen har fel format." | — |
| `VALIDATION_EMAIL_TAKEN` | "Den e-postadressen är redan registrerad." | — |
| `VALIDATION_PASSWORD_TOO_SHORT` | "Lösenordet måste vara minst 12 tecken." | — |
| `VALIDATION_PASSWORD_TOO_WEAK` | "Lösenordet är för svagt. Använd en kombination av bokstäver, siffror och specialtecken." | — |
| `VALIDATION_REQUIRED` | "Fältet är obligatoriskt." | Generisk, används av FormMessage |
| `VALIDATION_FILE_TOO_LARGE` | "Filen är för stor. Max {limit} MB." | Interpolera limit från svar |
| `VALIDATION_FILE_TYPE` | "Filformatet stöds inte. Ladda upp PDF eller Word (.docx)." | — |

---

## CV / Filer (RESUME_*)

| Code | Meddelande | Kommentar |
|---|---|---|
| `RESUME_NOT_FOUND` | "CV:t hittades inte." | Kan ha raderats |
| `RESUME_PARSE_FAILED` | "CV:t kunde inte läsas. Kontrollera att filen inte är skyddad eller skadad." | — |
| `RESUME_LIMIT_REACHED` | "Du har nått gränsen på {limit} sparade CV:n. Radera ett för att ladda upp ett nytt." | — |
| `RESUME_UPLOAD_FAILED` | "Uppladdningen misslyckades. Försök igen." | Generisk fallback |

---

## Ansökningar (APPLICATION_*)

| Code | Meddelande | Kommentar |
|---|---|---|
| `APPLICATION_NOT_FOUND` | "Ansökan hittades inte." | — |
| `APPLICATION_INVALID_STATUS_TRANSITION` | "Den här statusövergången är inte tillåten." | Ska inte visas om UX är korrekt |
| `APPLICATION_DUPLICATE` | "Du har redan en aktiv ansökan på den tjänsten." | — |

---

## AI (AI_*)

| Code | Meddelande | Kommentar |
|---|---|---|
| `AI_GENERATION_FAILED` | "Det gick inte att generera utkastet. Försök igen." | — |
| `AI_QUOTA_EXCEEDED` | "Du har nått din AI-kvot för den här månaden. Kvoten återställs {date}." | Interpolera datum |
| `AI_BYOK_INVALID` | "API-nyckeln är inte giltig. Kontrollera nyckeln i inställningarna." | — |
| `AI_BYOK_QUOTA_EXCEEDED` | "Din Anthropic-kvot är slut. Kontrollera din fakturering på console.anthropic.com." | — |
| `AI_CONTENT_FILTERED` | "Texten innehåller innehåll som inte kan behandlas. Redigera och försök igen." | — |
| `AI_TIMEOUT` | "AI-genereringen tog för lång tid. Försök igen." | — |

---

## Integrationer (INTEGRATION_*)

| Code | Meddelande | Kommentar |
|---|---|---|
| `INTEGRATION_GMAIL_AUTH_FAILED` | "Det gick inte att koppla Gmail. Försök igen." | — |
| `INTEGRATION_GMAIL_REVOKED` | "Åtkomsten till Gmail har återkallats. Koppla om kontot i inställningarna." | — |
| `INTEGRATION_GMAIL_SYNC_FAILED` | "Det gick inte att synkronisera Gmail. Försök igen om en stund." | — |

---

## Nätverk / Server (SYSTEM_*)

| Code | Meddelande | Kommentar |
|---|---|---|
| `SYSTEM_NETWORK_ERROR` | "Ingen anslutning. Kontrollera din nätverksanslutning." | Klient-side, ingen requestId |
| `SYSTEM_SERVER_ERROR` | "Ett fel uppstod (ID: {requestId}). Försök igen om en stund eller kontakta support om problemet kvarstår." | Interpolera requestId från svar |
| `SYSTEM_MAINTENANCE` | "Tjänsten är tillfälligt nere för underhåll. Vi är tillbaka inom kort." | — |
| `SYSTEM_RATE_LIMITED` | "För många förfrågningar. Vänta en stund och försök igen." | — |

---

## Okänt fel — fallback

Om frontend tar emot en okänd felkod:

```tsx
const message = errorCode
  ? `Ett oväntat fel uppstod (ID: ${requestId ?? "okänt"}). Kontakta support om problemet kvarstår.`
  : "Ingen anslutning. Kontrollera din nätverksanslutning."
```

Aldrig:
- `"Unknown error"`
- `"Error 500"`
- `"Something went wrong"`
- Visa `code`-värdet direkt för användaren
