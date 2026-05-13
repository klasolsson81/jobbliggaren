# ---------------------------------------------------------------------------
# Vercel DNS-records — frontend-host för jobbpilot.se + www.jobbpilot.se.
# Bor i prod/baseline-stacken (delad global resurs likt KMS + Route53-zone
# per ADR 0026). Stack-mappen heter `prod/` men fungerar de facto som
# baseline för alla miljöer.
#
# Vercel-hosting för Next.js frontend (web/jobbpilot-web). Klas-beslut
# 2026-05-13: civic-utility-konvention med apex som primary-redirect-target,
# www som production. Domain-strategi A per CC-rond 2026-05-13.
#
# Vercel-projekt:
#   - Team: Klas' projects (Hobby)
#   - Project: jobbpilot
#   - Default-domain: jobbpilot.vercel.app (kvar som backup)
#   - BACKEND_URL env-var i Vercel: https://dev.jobbpilot.se
#
# Vercel-config:
#   - jobbpilot.se → 301 Moved Permanently → www.jobbpilot.se
#   - www.jobbpilot.se → Production (faktisk Next.js-app)
#
# Records levereras av Vercel — projekt-specifika targets (inte default
# cname.vercel-dns.com / 76.76.21.21). Vercels Domains-vy ger exakta värden.
# Ändras targets av Vercel: uppdatera dessa records + apply.
#
# OBS: prod backend-stack är deferred till Fas 7-prep per ADR 0036 D1.
# Frontend pekar på dev-backend tills prod-backend etablerad. Cutover sker
# via uppdatering av Vercel BACKEND_URL env-var (ingen DNS-ändring krävs).
#
# Kostnad: ingår i hosted zone $0.50/mån (Route53). Vercel Hobby = $0.
# ---------------------------------------------------------------------------

# TTL=300 (5 min) för apex+www = snabbt rollback-fönster vid mis-config.
# Höj till 3600 efter stabil drift (~1 mån utan ändringar). Route53-cost-
# skillnad vid JobbPilots volym är försumbar (<$0.01/mån).

# Apex jobbpilot.se → Vercel statisk IP. CNAME inte tillåten på apex per
# RFC 1034; A-record till Vercel Anycast krävs. Vercel ger projekt-specifik
# IP (216.198.79.1) — del av planerad IP-range-expansion. Default 76.76.21.21
# fortsätter fungera men nya rekommenderas (per Vercel Domains-vy).
resource "aws_route53_record" "vercel_apex" {
  zone_id = module.route53.zone_id
  name    = var.domain_name
  type    = "A"
  ttl     = 300
  records = ["216.198.79.1"]
}

# www.jobbpilot.se → Vercel projekt-specifik CNAME. Notera: targeten är
# unik per Vercel-projekt (innehåller projekt-hash) — inte den generiska
# cname.vercel-dns.com. Vid Vercel-projekt-ombyggnation: uppdatera value.
resource "aws_route53_record" "vercel_www" {
  zone_id = module.route53.zone_id
  name    = "www.${var.domain_name}"
  type    = "CNAME"
  ttl     = 300
  records = ["9b8a4671a5ca7fcc.vercel-dns-017.com"]
}

# CAA-record (RFC 8659) — låser cert-utfärdande till Let's Encrypt (Vercels
# CA) för hela *.jobbpilot.se. Defense-in-depth mot cert-mis-issuance vid
# CA-kompromiss eller domain-takeover-försök hos annan CA. Saltzer/Schroeder
# 1975 default-deny — utan CAA är ALLA CA tillåtna utfärda cert för domänen.
# Source: security-auditor Vercel-DNS-review 2026-05-13.
#
# `iodef` (incident-rapport-email till CA vid policy-violation) tillagd när
# security@jobbpilot.se-mailbox finns. Tills dess: bara `issue`-direktiv.
resource "aws_route53_record" "caa" {
  zone_id = module.route53.zone_id
  name    = var.domain_name
  type    = "CAA"
  ttl     = 300
  records = [
    "0 issue \"letsencrypt.org\"",
  ]
}
