using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Identity.Api;

internal sealed class SanitizedExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : 500;
        ProblemDetails problem = status == 400
            ? new HttpValidationProblemDetails(new Dictionary<string, string[]> { ["body"] = ["The request body has unknown fields or invalid values."] })
            : new ProblemDetails();
        problem.Status = status;
        problem.Title = status < 500 ? "Invalid request." : "An unexpected error occurred.";
        problem.Extensions["code"] = status < 500 ? "request.invalid" : "internal_error";
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
