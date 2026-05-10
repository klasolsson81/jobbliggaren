# ---------------------------------------------------------------------------
# Route53 hosted zone — apex-zone för JobbPilot-domänen.
# Bor i baseline-stacken (prod/baseline.tfstate) som global delad resurs.
# Dev/staging/prod-stacks gor `data "aws_route53_zone"`-lookup mot zonen.
#
# Domänen registreras separat hos svensk registrar (Loopia/Inleed/Binero).
# Efter att zonen skapats: kopiera output `name_servers` (4 NS-records) och
# peka registrar's NS-records till AWS. DNS-propagering ~30 min.
#
# Kostnad: $0.50/månad per hosted zone + $0.40 per miljon queries.
# ---------------------------------------------------------------------------

resource "aws_route53_zone" "this" {
  name    = var.domain_name
  comment = var.comment

  tags = merge(var.tags, {
    Name = var.domain_name
  })
}
