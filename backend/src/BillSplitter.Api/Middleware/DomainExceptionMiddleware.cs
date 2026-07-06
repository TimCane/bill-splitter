using BillSplitter.Api.Http;
using BillSplitter.Domain;

namespace BillSplitter.Api.Middleware;

/// <summary>Turns a <see cref="DomainException"/> into an RFC 7807 response with
/// the domain code as <c>type</c> (docs/04-api-contract.md#errors).</summary>
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
    }
}
