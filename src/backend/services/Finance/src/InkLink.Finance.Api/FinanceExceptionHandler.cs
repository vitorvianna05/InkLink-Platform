using InkLink.Finance.Application;
using InkLink.Finance.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Finance.Api;

internal sealed class FinanceExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            InvalidFinanceData => 400,
            BadHttpRequestException bad => bad.StatusCode,
            FinanceFailure failure => failure.Error switch { FinanceError.Invalid => 400, FinanceError.Unauthenticated => 401, FinanceError.Forbidden => 403, FinanceError.NotFound => 404, FinanceError.Conflict => 409, FinanceError.Unavailable => 503, _ => 500 },
            _ => 500
        };
        var errors = exception switch
        {
            InvalidFinanceData invalid => new Dictionary<string, string[]> { [invalid.Field] = [invalid.Message] },
            FinanceFailure failure when failure.Errors.Count > 0 => failure.Errors,
            BadHttpRequestException => new() { ["request"] = ["Request contains unknown fields or invalid values."] },
            _ => null
        };
        ProblemDetails problem = errors is not null ? new HttpValidationProblemDetails(errors) : new ProblemDetails();
        problem.Status = status;
        problem.Title = status < 500 ? "Unable to complete this request." : "Service unavailable or unexpected error.";
        problem.Extensions["code"] = exception is FinanceFailure known ? known.Code : status < 500 ? "finance.invalid" : "internal_error";
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
