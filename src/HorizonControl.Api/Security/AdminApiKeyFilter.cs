using HorizonControl.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace HorizonControl.Api.Security;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdminApiKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Admin-Api-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<FleetControlOptions>>().Value;
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            !string.Equals(provided.ToString(), options.AdminApiKey, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or missing admin API key." });
            return;
        }

        await next();
    }
}
