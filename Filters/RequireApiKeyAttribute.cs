using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace StanTrack.Filters
{
    // Header-based API key gate for write endpoints. Compared in fixed time
    // via CryptographicOperations.FixedTimeEquals so the response time does
    // not leak prefix-match information about the key.
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class RequireApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
    {
        private const string HeaderName = "Key";
        private readonly string _configKey;

        public RequireApiKeyAttribute(string configKey)
        {
            _configKey = configKey;
        }

        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var expected = config[$"ApiKeys:{_configKey}"];

            if (string.IsNullOrEmpty(expected))
            {
                // Misconfigured server: fail closed, not open.
                context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
                return Task.CompletedTask;
            }

            if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
                || string.IsNullOrEmpty(provided))
            {
                context.Result = new UnauthorizedResult();
                return Task.CompletedTask;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());

            if (expectedBytes.Length != providedBytes.Length
                || !CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
            {
                context.Result = new UnauthorizedResult();
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }
    }
}
