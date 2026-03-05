namespace MonopolyServer.Infrastructure.Budget;

/// <summary>
/// ASP.NET Core middleware that enforces the monthly HTTP request budget.
/// Returns 503 with a Retry-After header when the limit is reached.
/// Must be registered before UseRouting so it covers all endpoints.
/// </summary>
public sealed class BudgetMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BudgetGuardService _budget;

    /// <summary>
    /// Injected via constructor — BudgetGuardService is a singleton.
    /// </summary>
    public BudgetMiddleware(RequestDelegate next, BudgetGuardService budget)
    {
        _next   = next;
        _budget = budget;
    }

    /// <summary>
    /// Counts the request against the monthly budget and short-circuits with 503 if exhausted.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_budget.TryConsumeRequest())
        {
            var retryAfter = (int)(BudgetDocument.GetCurrentMonthStart().AddMonths(1) - DateTime.UtcNow).TotalSeconds;
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = retryAfter.ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                error   = "Monthly request budget exhausted. Service resumes next billing cycle.",
                resetAt = BudgetDocument.GetCurrentMonthStart().AddMonths(1),
            });
            return;
        }

        await _next(context);
    }
}