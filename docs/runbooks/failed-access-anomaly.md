# Runbook — Failed access anomaly-alarm

**Skapad:** 2026-05-12 (TD-68 / ADR 0031)
**Owner:** Klas (Fas 1) — överlämnas till SecOps-roll vid Fas 2-bemanning
**SNS-topic:** `${name_prefix}-secops-anomaly`
**Alarms täckta:**
- `${name_prefix}-failed-access-anomaly` — BOLA-enumeration-attack-signal
- `${name_prefix}-api-log-pipeline-health` — log-pipeline-fel (kompletterar
  anomaly-alarmet)

## Bakgrund

ADR 0031 etablerar `IFailedAccessLogger`-port som skriver strukturerade
events (`event_name=failed_access_attempt`) när ownership-check misslyckas
i Application-handlers (cross-user-access mot annan users resurs).

CloudWatch metric filter aggregerar events till `JobbPilot/Security/
FailedAccessAttempts`-metric. Alarm triggar vid threshold-överträdelse
över period.

## Vid alarm-trigger

### Steg 1 — Klassificera (5 min)

Är det en attack eller false-positive?

```bash
# Per-user-drill-down: vilka users genererar flest failed-access-events?
aws logs start-query \
  --log-group-name /aws/ecs/jobbpilot-dev/api \
  --start-time $(date -u -d '15 minutes ago' +%s) \
  --end-time $(date -u +%s) \
  --query-string 'fields @timestamp, @message
                  | filter @message like /event_name=failed_access_attempt/
                  | parse @message "requesting_user_id=* operation=*" as user_id, op
                  | stats count() by user_id, op
                  | sort count() desc
                  | limit 20'
```

**Tolkning:**

- **Singel user, många events:** sannolik BOLA-enumeration → Steg 2
- **Många users, få events vardera:** sannolik distributed attack ELLER
  legitim hög-trafik-rampup (t.ex. demo-event, marketing-launch) → Steg 3
- **Inga events i query-resultatet:** false-positive (alarm-fel eller
  CloudWatch-evaluering-glitch) → Steg 4

### Steg 2 — BOLA-enumeration (singel user attacks)

1. Identifiera angriparen via `requesting_user_id` (Guid) — slå upp i
   `users`-tabellen för email + last-login-IP.
2. Tillfällig session-invalidering: kör `INVALIDATE_USER`-flow (Fas 2 admin-
   endpoint — tills dess: revoke session i Redis manuellt via Redis-CLI).
3. Granska om angriparen lyckats få access till någon annan users data:
   ```bash
   # Bevis att 404 returnerades (inte 200) för alla cross-user-attempts
   aws logs start-query \
     --log-group-name /aws/ecs/jobbpilot-dev/api \
     --query-string 'filter @message like /requesting_user_id=<USER_ID>/
                     | filter status >= 400 or status < 200'
   ```
4. Skapa incident-rapport i `docs/security-incidents/YYYY-MM-DD-bola-*.md`
   med tidslinje + åtgärder + roten-orsak.
5. **Risk-bedömning enligt GDPR Art. 33:** notification krävs "unless the
   breach is unlikely to result in a risk to rights and freedoms" (Art. 33(1)).
   404 returnerat = ingen data-läckage = troligen ingen notification-plikt.
   Logg-event indikerar dock attack-avsikt — dokumentera bedömningen explicit.
6. Om bekräftad data-läckage (200-svar på cross-user-request): **GDPR Art. 33
   incident-notification till IMY** (Integritetsskyddsmyndigheten, sedan 2021
   ersatte Datainspektionen) inom 72h-fönstret från upptäckt.

### Steg 3 — Distributed attack eller legitim trafik

1. Verifiera om events är spridda över IPs (CloudTrail) eller koncentrerade.
2. Om koncentrerad IP/ASN: tillfällig WAF-block i ALB.
3. Om spridd (möjlig distributed attack): höj rate-limit-policy i Api,
   monitora 24h.
4. Om legitim (demo-launch, etc.): justera `failed_access_alarm_threshold`
   uppåt tillfälligt + dokumentera i `docs/security-incidents/` som
   false-positive.

### Steg 4 — Log-pipeline-health (`api-log-pipeline-health`-alarm)

Om `${name_prefix}-api-log-pipeline-health` triggar betyder det att
api-log-gruppen inte tar emot events. Anomaly-alarmet är då bevisbart
icke-funktionellt (no data = no detection).

1. Verifiera ECS-task-status: `aws ecs describe-services --cluster jobbpilot-dev --services api`
2. Verifiera FluentBit/CloudWatch-agent-status (om används).
3. Verifiera IAM-policy: api-task-role har `logs:CreateLogStream` +
   `logs:PutLogEvents` mot api-log-gruppen?
4. Om ECS-tasks är döda: rolling-deploy via ECR-image-tag.
5. Om IAM-policy är fel: granska `module.iam_ecs`-konfiguration.

## Pre-apply-verifiering (innan första `terraform apply`)

Innan `terraform apply` av denna modul i ny miljö — verifiera att metric
filter-pattern matchar FailedAccessLogger:s faktiska output:

```bash
aws logs test-metric-filter \
  --filter-pattern 'event_name=failed_access_attempt' \
  --log-event-messages 'Failed cross-user access attempt: event_name=failed_access_attempt aggregate_type=Application requested_aggregate_id=550e8400-e29b-41d4-a716-446655440000 requesting_user_id=11111111-1111-1111-1111-111111111111 operation=GetApplicationById'
```

Förväntat: ett match i output. Om noll match → fixa filter-pattern innan
deploy.

## SNS-subscription-rotation

SNS-email-subscriptions kvarstår tills manuellt borttagna. Vid personalbyte:

1. List nuvarande subscriptions:
   ```bash
   aws sns list-subscriptions-by-topic --topic-arn <secops-anomaly-arn>
   ```
2. Unsubscribe via console eller `aws sns unsubscribe --subscription-arn ...`
3. Subscribe ny mottagare via `secops_alert_email` i `terraform.tfvars` +
   `terraform apply` + opt-in via AWS-mail.

Granska aktiv subscription-lista kvartalsvis (kalender-event för Klas).

## Drift-tester (post-apply)

Efter första apply: verifiera end-to-end-flow manuellt.

1. Logga in som test-user A.
2. Skapa en Application (få Application-ID).
3. Logga ut, registrera test-user B.
4. Logga in som B, gör cross-user-anrop:
   `curl -H "Cookie: __Host-jobbpilot_session=<B-session>" https://<env>/api/v1/applications/<A-application-id>`
5. Förvänta 404 + log-event `event_name=failed_access_attempt`.
6. Verifiera i CloudWatch metric: `FailedAccessAttempts` ökar med 1.
7. Trigga upprepad attack (51 anrop/min för dev-default threshold 50).
8. Förvänta alarm-trigger inom ~60s → SNS-meddelande (om subscription finns).

## Severity-klassificering

| Pattern | Severity | Respons |
|---|---|---|
| Singel user > 50/min | HIGH | Tillfällig user-block + incident-rapport |
| Singel user 20-50/min | MEDIUM | Övervaka 1h; om kvar → block |
| Spridd över >5 users | MEDIUM | Verifiera IP/ASN; ev. WAF-justering |
| Spridd över >20 users | LOW (men kontrollera!) | Sannolik legitim trafik-spike |
| Log-pipeline-alarm | HIGH | ECS/IAM-triage omedelbart |
