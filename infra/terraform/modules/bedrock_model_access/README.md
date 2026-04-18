# bedrock_model_access

IAM-grunden för att anropa Anthropic-modeller via AWS Bedrock EU cross-region
inference profile. Själva *access-approvalet* är en manuell process — modulen
sätter upp IAM-policyn som appen attachar sin run-task-roll till när access
väl finns.

## Manuell approval

Se [`docs/runbooks/aws-setup.md`](../../../../docs/runbooks/aws-setup.md) §3.1.
Kortversion:

1. AWS Console → Bedrock → Model access (region `eu-central-1`).
2. Request access till Haiku 4.5 + Sonnet 4.6 (+ Opus 4.7 om tillgänglig).
3. Vänta på approval-mail (minuter till timmar).
4. Upprepa i `eu-west-1`.
5. Verifiera:
   ```bash
   aws bedrock list-inference-profiles --region eu-central-1 --profile jobbpilot
   aws bedrock list-inference-profiles --region eu-west-1   --profile jobbpilot
   ```
6. Spara outputs i `docs/research/bedrock-inference-profiles.md`
   (SESSION-2-PLAN §14).

## Policy-omfång

Tillåter:
- `bedrock:InvokeModel`, `InvokeModelWithResponseStream`
- `bedrock:Converse`, `ConverseStream`
- `bedrock:List*`, `Get*` (read-only metadata)

Mot:
- EU inference profile-ARNs i `var.eu_inference_profile_ids`
- Underliggande foundation-models (`anthropic.claude-*`) i varje source-region

## Framtid

När vi har ECS-tasks kommer roll-attaching ske där:

```hcl
resource "aws_iam_role_policy_attachment" "api_bedrock" {
  role       = aws_iam_role.api_task.name
  policy_arn = module.bedrock_model_access.bedrock_invoke_policy_arn
}
```
