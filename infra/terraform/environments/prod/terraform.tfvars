notification_emails = [
  "klasolsson81@gmail.com",
]

# Höjd 2026-05-09 inför STEG 13a apply: dev-stacken (RDS Multi-AZ + Redis 2-node
# + NAT + 3 Interface VPC Endpoints) ger ~$140/mån baseline utan trafik. $50-default
# triggar 100% ACTUAL-alert direkt. $200 ger margin för STEG 13b-tillägg
# (ECS-tasks + ALB + CloudFront-möjlig framtid) utan att alarm-spammar.
monthly_budget_usd = 200

# cloudtrail_retention_days = 90       # default
# eu_inference_profile_ids  = [...]    # default, uppdatera efter Bedrock-approval
