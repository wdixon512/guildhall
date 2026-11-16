using System.Security.Cryptography;
using System.Text;
using Guildhall.AgentHost.Configuration;

namespace Guildhall.AgentHost.Api;

public static class LocalApiBoundary
{
    public static void UseLocalBoundary(this WebApplication app, Uri hostUri, string token)
    {
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes("Bearer " + token));
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Host != hostUri.Host || context.Request.Host.Port != hostUri.Port)
            { context.Response.StatusCode = 400; return; }

            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && !LocalConfiguration.Origins.Contains(origin, StringComparer.Ordinal))
            { context.Response.StatusCode = 403; return; }
            await next(context);
        });
        app.UseCors();
        app.Use(async (context, next) =>
        {
            var supplied = context.Request.Headers.Authorization.ToString();
            if (supplied.Length > 256 || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
            { context.Response.StatusCode = 401; return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context);
        });
    }
}
