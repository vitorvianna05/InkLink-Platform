using InkLink.Engagements.Application;
using InkLink.Engagements.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Engagements.Api;

internal sealed class EngagementsExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            InvalidEngagementData => 400,
            BadHttpRequestException bad => bad.StatusCode,
            EngagementsFailure failure => failure.Error switch { EngagementsError.Invalid => 400, EngagementsError.Unauthenticated => 401, EngagementsError.Forbidden => 403, EngagementsError.NotFound => 404, EngagementsError.Conflict => 409, EngagementsError.Unavailable => 503, _ => 500 },
            _ => 500
        };
        var errors = exception switch
        {
            InvalidEngagementData invalid => new Dictionary<string, string[]> { [invalid.Field] = [invalid.Message] },
            EngagementsFailure failure when failure.Errors.Count > 0 => failure.Errors,
            BadHttpRequestException => new() { ["request"] = ["Request contains unknown fields or invalid values."] },
            _ => null
        };
        ProblemDetails problem = errors is not null ? new HttpValidationProblemDetails(errors) : new ProblemDetails();
        problem.Status = status;
        problem.Title = status < 500 ? "Unable to complete this request." : "Service unavailable or unexpected error.";
        problem.Extensions["code"] = exception is EngagementsFailure known ? known.Code : status < 500 ? "engagements.invalid" : "internal_error";
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
