using System.Text.Json;
using BillSplitter.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace BillSplitter.Api.Http;

/// <summary>Maps stable error codes to HTTP status and writes RFC 7807
/// problem+json with the code as <c>type</c> (docs/04-api-contract.md#errors).</summary>
public static class ApiProblem
{
    public static int StatusFor(string code) => code switch
    {
        ErrorCodes.Validation => StatusCodes.Status400BadRequest,
        ErrorCodes.MissingToken => StatusCodes.Status401Unauthorized,
        ErrorCodes.NotHost => StatusCodes.Status403Forbidden,
        ErrorCodes.UnknownParticipant => StatusCodes.Status403Forbidden,
        ErrorCodes.SessionNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.ItemNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.ReceiptNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.WrongState => StatusCodes.Status409Conflict,
        ErrorCodes.SessionFull => StatusCodes.Status409Conflict,
        ErrorCodes.ImageTooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
        ErrorCodes.ConflictRetryExhausted => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static async Task WriteAsync(HttpContext context, string code, string? detail = null)
    {
        var status = StatusFor(code);
        var problem = new ProblemDetails
        {
            Type = code,
            Title = code,
            Status = status,
            Detail = detail,
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions), context.RequestAborted);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
