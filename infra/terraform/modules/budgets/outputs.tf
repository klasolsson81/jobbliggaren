output "zero_spend_budget_name" {
  value = aws_budgets_budget.zero_spend.name
}

output "monthly_budget_name" {
  value = aws_budgets_budget.monthly.name
}
