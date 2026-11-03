using InkLink.Catalog.Application;
using InkLink.Catalog.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Catalog.Api;

internal sealed class CatalogExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            InvalidCatalogData => 400,
            BadHttpRequestException bad => bad.StatusCode,
            CatalogFailure failure => failure.Error switch { CatalogError.Invalid => 400, CatalogError.Unauthenticated => 401, CatalogError.Forbidden => 403, CatalogError.NotFound => 404, CatalogError.Conflict => 409, CatalogError.Unavailable => 503, _ => 500 },
            _ => 500
        };
        var errors = exception switch
        {
            InvalidCatalogData invalid => new Dictionary<string, string[]> { [invalid.Field] = [invalid.Message] },
            CatalogFailure failure when failure.Errors.Count > 0 => failure.Errors,
            BadHttpRequestException => new() { ["request"] = ["Request contains unknown fields or invalid values."] },
            _ => null
        };
        ProblemDetails problem = errors is not null ? new HttpValidationProblemDetails(errors) : new ProblemDetails();
        problem.Status = status;
        problem.Title = status < 500 ? "Unable to complete this request." : "Service unavailable or unexpected error.";
        problem.Extensions["code"] = exception is CatalogFailure known ? known.Code : status < 500 ? "catalog.invalid" : "internal_error";
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
