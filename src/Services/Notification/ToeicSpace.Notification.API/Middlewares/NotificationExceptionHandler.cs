using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ToeicSpace.Notification.API.Middlewares;

public sealed class NotificationExceptionHandler(ILogger<NotificationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception error, CancellationToken ct)
    {
        ProblemDetails problem;
        if (error is ValidationException validation)
        {
            var errors = validation.Errors.GroupBy(x => x.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(x => x.ErrorMessage).Distinct().ToArray());
            problem = new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest };
            problem.Extensions["code"] = "validation_error";
        }
        else
        {
            logger.LogError(error, "Unhandled Notification error");
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal Server Error",
                Detail = "An unexpected error occurred."
            };
            problem.Extensions["code"] = "internal_error";
        }

        problem.Instance = context.Request.Path;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
