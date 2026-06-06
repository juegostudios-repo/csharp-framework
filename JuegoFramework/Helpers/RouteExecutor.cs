
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JuegoFramework.Helpers
{
    public class RouteExecutor
    {
        public static async Task<IActionResult> Execute(string method, string route, string bodyJSON, string? accessToken = null)
        {
            var routeFound = Global.EndpointSources!
            .SelectMany(es => es.Endpoints)
            .OfType<RouteEndpoint>().Where(
                e =>
                {
                    return e.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods?[0].ToLower() == method.ToLower() &&
                    e.RoutePattern.RawText == route;
                }
            ).FirstOrDefault() ?? throw new Exception("Route not found");

            var controllerMetadata = routeFound.Metadata
                .OfType<ControllerActionDescriptor>()
                .FirstOrDefault() ?? throw new Exception("Route not found");

            var controllerType = controllerMetadata.ControllerTypeInfo.AsType();
            MethodInfo methodInfo = controllerMetadata.MethodInfo;

            // One DI scope per routed message, mirroring MVC's per-request scope, so the
            // controller and its scoped dependencies (e.g. scoped services, DbContext)
            // resolve correctly. Building the controller from the root provider throws on
            // scoped services under scope validation (Development) and silently shares them
            // as singletons in Production. The scope lives until the action completes below.
            using var scope = Global.ServiceProvider!.CreateScope();
            var serviceProvider = scope.ServiceProvider;

            var controller = ActivatorUtilities.CreateInstance(serviceProvider, controllerType) as ControllerBase ?? throw new Exception("Route not found");

            if (accessToken != null)
            {
                controller.ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                };

                controller.ControllerContext.HttpContext.Request.Headers.Append("access_token", accessToken);
            }

            var authAttributes = controllerMetadata.MethodInfo.GetCustomAttributes(typeof(IAsyncAuthorizationFilter), true)
                .Concat(controllerMetadata.ControllerTypeInfo.GetCustomAttributes(typeof(IAsyncAuthorizationFilter), true))
                .OfType<IAsyncAuthorizationFilter>()
                .ToList();

            if (authAttributes != null)
            {
                foreach (var authAttribute in authAttributes)
                {
                    // Resolve the auth filter from the same per-message scope as the controller.
                    var authInstance = (IFilterMetadata)serviceProvider.GetRequiredService(authAttribute.GetType());

                    var authFilterContext = new AuthorizationFilterContext(
                        new ActionContext(controller.ControllerContext.HttpContext, new RouteData(), controllerMetadata),
                        [authInstance]
                    );

                    // Cast to IAsyncAuthorizationFilter or IAuthorizationFilter
                    if (authInstance is IAsyncAuthorizationFilter asyncAuthFilter)
                    {
                        await asyncAuthFilter.OnAuthorizationAsync(authFilterContext);
                    }
                    else if (authInstance is IAuthorizationFilter syncAuthFilter)
                    {
                        syncAuthFilter.OnAuthorization(authFilterContext);
                    }

                    if (authFilterContext.Result != null)
                    {
                        return authFilterContext.Result;
                    }
                }
            }

            var parameterInfo = methodInfo.GetParameters().FirstOrDefault();

            object[] parameters = [];
            if (parameterInfo != null)
            {
                var parameterType = parameterInfo.ParameterType;

                var deserializedParameter = JsonSerializer.Deserialize(bodyJSON, parameterType) ?? throw new Exception("Unable to parse bodyJSON to destination route method parameter");

                parameters = [deserializedParameter];
            }

            var result = methodInfo.Invoke(controller, parameters) as Task<IActionResult>;

            return result?.Result ?? throw new Exception("Route method invocation returned null");
        }
    }
}
