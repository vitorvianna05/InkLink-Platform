using InkLink.Messaging.Application;
using InkLink.Messaging.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InkLink.Messaging.Api;

internal sealed class MessagingExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            InvalidConversationData => 400,
            BadHttpRequestException bad => bad.StatusCode,
            MessagingFailure failure => failure.Error switch { MessagingError.Invalid => 400, MessagingError.Unauthenticated => 401, MessagingError.Forbidden => 403, MessagingError.NotFound => 404, MessagingError.Conflict => 409, MessagingError.Unavailable => 503, _ => 500 },
            _ => 500
        };
        var errors = exception switch
        {
            InvalidConversationData invalid => new Dictionary<string, string[]> { [invalid.Field] = [invalid.Message] },
            MessagingFailure failure when failure.Errors.Count > 0 => failure.Errors,
            BadHttpRequestException => new() { ["request"] = ["Request contains unknown fields or invalid values."] },
            _ => null
        };
        ProblemDetails problem = errors is not null ? new HttpValidationProblemDetails(errors) : new ProblemDetails();
        problem.Status = status;
        problem.Title = status < 500 ? "Unable to complete this request." : "Service unavailable or unexpected error.";
        problem.Extensions["code"] = exception is MessagingFailure known ? known.Code : status < 500 ? "messaging.invalid" : "internal_error";
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
