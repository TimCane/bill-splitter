using BillSplitter.Api.Http;
using BillSplitter.Domain.Common;

namespace BillSplitter.Api.Middleware;

/// <summary>Turns a <see cref="DomainException"/> into an RFC 7807 response with
/// the domain code as <c>type</c> (docs/04-api-contract.md#errors). Also maps the
/// Kestrel body-size rejection to the same <c>image-too-large</c> 413 the explicit
/// check emits, so an oversized upload reads identically however it is caught
/// (docs/10-security-privacy.md#upload-hardening).</summary>
public sealed class DomainExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            await ApiProblem.WriteAsync(context, ex.Code, ex.Detail);
        }
        catch (BadHttpRequestException ex)
            when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            await ApiProblem.WriteAsync(context, ErrorCodes.ImageTooLarge, "upload exceeds the size limit");
        }
    }
}
