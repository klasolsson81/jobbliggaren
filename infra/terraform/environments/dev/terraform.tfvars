# Defaults i variables.tf räcker för dev. Lägg overrides här om något behöver ändras.
#
# Exempel:
#   rds_instance_class = "db.t4g.small"   # billigare för utvecklingstest
#   redis_node_type    = "cache.t4g.micro"

# ---------------------------------------------------------------------------
# STEG 13c — HTTPS-flip 2026-05-10 (ADR 0026 trigger 1 → ADR 0027)
#
# ACM-cert validerat 2026-05-10 via Route53 DNS-validation. Flippar
# ALB-HTTP-listenern till HTTPS-redirect via dynamic-block i modules/alb/main.tf.
# Samtidigt injiceras Alb__HttpsEnabled=true som env-var i Api-task-def vilket
# aktiverar app.UseHsts() + app.UseHttpsRedirection() i ASP.NET-pipelinen.
# ---------------------------------------------------------------------------
alb_https_enabled       = true
alb_acm_certificate_arn = "arn:aws:acm:eu-north-1:710427215829:certificate/f72a79d7-f964-49c7-abb5-cf81b8639d6a"

# ---------------------------------------------------------------------------
# STEG 14b — Migrate one-shot DDL-init 2026-05-10
#
# Image pushad till ECR med tag 14b-<git-sha>. Sätter migrate_image_tag
# aktiverar count = 1 i modules/ecs/aws_ecs_task_definition.migrate.
# ---------------------------------------------------------------------------
migrate_image_tag = "14b-9113bed-fix3"

# ---------------------------------------------------------------------------
# TD-68 — Security anomaly detection (ADR 0031)
#
# secops_alert_email lämnas tom så ingen SNS-subscription skapas vid first apply
# (subscription-confirmation kräver manuell opt-in via AWS-mail oavsett).
# När redo: sätt email här + kör `terraform apply`, sen confirma AWS-mail.
# ---------------------------------------------------------------------------
secops_alert_email            = ""
failed_access_alarm_threshold = 50

# ---------------------------------------------------------------------------
# F2-P3 — Cost controls (Budget Actions, ADR 0005 second amendment 2026-05-12)
#
# cost_anomaly_alert_email lämnas tom vid first apply — Klas kan lägga till
# senare och re-applya. Confirmation-mail från AWS måste opt-in:as manuellt.
# baseline_budget_name pekar på "jobbpilot-monthly" som ägs av prod/baseline-
# stacken (modules/budgets/aws_budgets_budget.monthly).
# ---------------------------------------------------------------------------
cost_anomaly_alert_email = ""
baseline_budget_name     = "jobbpilot-monthly"

# ---------------------------------------------------------------------------
# F2-P8b — Admin-bootstrap (ADR 0028 + AdminBootstrap-mekanism 2026-05-11)
#
# IdempotentAdminRoleSeeder tilldelar Admin-rollen vid host-startup till user
# med matchande email. Värdet injiceras som env-var
# AdminBootstrap__InitialAdminEmail i Api-task-def (icke-känsligt — lösenord
# sätts vid registrering av Klas själv). Synkroniserat 2026-05-13 inför
# admin-trigger-smoke-test mot /api/v1/admin/job-ads/sync/platsbanken.
# ---------------------------------------------------------------------------
initial_admin_email = "klasolsson81@gmail.com"

# ---------------------------------------------------------------------------
# ADR 0066 (2026-05-26) — semester-pause teardown.
# api_image_tag + worker_image_tag har validation `length > 0 && != "latest"`.
# För `terraform destroy` behöver dessa giltiga värden för att passera
# validation vid plan/destroy (även när task-defs ska raderas).
# Dummy-värdet "teardown" passes validation och signalerar tydligt att
# tfvars är i teardown-state.
# Vid återstart: TA BORT dessa rader (sätts som -var av deploy-workflow).
# ---------------------------------------------------------------------------
api_image_tag    = "teardown"
worker_image_tag = "teardown"
