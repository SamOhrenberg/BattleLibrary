namespace BattleLibrary.Api.Catalogue;

// Gates admin endpoints behind a shared key (X-Admin-Key header). When no key
// is configured the endpoints are effectively closed.
public sealed class AdminKeyFilter(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration["Admin:ApiKey"];
        var provided = context.HttpContext.Request.Headers["X-Admin-Key"].ToString();

        if (string.IsNullOrEmpty(expected) || provided != expected)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        return await next(context);
    }
}
