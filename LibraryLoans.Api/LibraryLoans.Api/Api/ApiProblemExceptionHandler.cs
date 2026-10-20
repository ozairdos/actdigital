using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LibraryLoans.Api.Api;

public sealed class ApiProblemExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ApiProblemException apiException)
        {
            logger.LogError(exception, "Unhandled request error. CorrelationId: {CorrelationId}", httpContext.TraceIdentifier);
            return false;
        }

        httpContext.Response.StatusCode = apiException.StatusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = apiException.StatusCode,
                Title = apiException.Title,
                Detail = apiException.Message,
                Instance = httpContext.Request.Path
            }
        });

        return true;
    }
}