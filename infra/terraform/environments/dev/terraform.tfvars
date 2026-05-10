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
