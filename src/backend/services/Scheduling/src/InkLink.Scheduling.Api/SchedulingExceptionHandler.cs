using InkLink.Scheduling.Application;
using InkLink.Scheduling.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Scheduling.Api;

internal sealed class SchedulingExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            InvalidScheduleData => 400,
            BadHttpRequestException bad => bad.StatusCode,
            SchedulingFailure failure => failure.Error switch { SchedulingError.Invalid => 400, SchedulingError.Unauthenticated => 401, SchedulingError.Forbidden => 403, SchedulingError.NotFound => 404, SchedulingError.Conflict => 409, SchedulingError.Unavailable => 503, _ => 500 },
            _ => 500
        };
        var errors = exception switch
        {
            InvalidScheduleData invalid => new Dictionary<string, string[]> { [invalid.Field] = [invalid.Message] },
            SchedulingFailure failure when failure.Errors.Count > 0 => failure.Errors,
            BadHttpRequestException => new() { ["request"] = ["Request contains unknown fields or invalid values."] },
            _ => null
        };
        ProblemDetails problem = errors is not null ? new HttpValidationProblemDetails(errors) : new ProblemDetails();
        problem.Status = status;
        problem.Title = status < 500 ? "Unable to complete this request." : "Service unavailable or unexpected error.";
        problem.Extensions["code"] = exception is SchedulingFailure known ? known.Code : status < 500 ? "scheduling.invalid" : "internal_error";
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
