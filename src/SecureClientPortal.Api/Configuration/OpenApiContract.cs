using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Writers;
using SecureClientPortal.Backend.Api.Modules.Banking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swashbuckle.AspNetCore.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureClientPortal.Backend.Api.Configuration;

public static class OpenApiContract
{
    public static IServiceCollection AddPortalOpenApi(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo { Title = "Secure Client Portal API", Version = "v1",
                Description = "Portal HTTP contracts. Cookie-authenticated writes require a CSRF token from /api/auth/csrf in X-CSRF-Token. Bank integration remains paused; manual statement imports are supported." });
            options.CustomSchemaIds(SchemaId);
            options.CustomOperationIds(api => api.TryGetMethodInfo(out var method)
                ? $"{api.ActionDescriptor.RouteValues["controller"]}_{method.Name}_{api.HttpMethod}_" +
                    System.Text.RegularExpressions.Regex.Replace(api.RelativePath ?? "", "[^a-zA-Z0-9]+", "_").Trim('_') : null);
            options.SupportNonNullableReferenceTypes();
            options.SchemaFilter<ContractRequiredProperties>();
            options.OperationFilter<ContractSecurity>();
            options.OperationFilter<SourceResponseMetadata>();
            options.DocumentFilter<SourceResponseMetadata>();
            var comments = Path.Combine(AppContext.BaseDirectory, "SecureClientPortal.Api.xml");
            if (File.Exists(comments)) options.IncludeXmlComments(comments);
            options.AddSecurityDefinition("portalCookie", new OpenApiSecurityScheme {
                Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = SecureClientPortal.Backend.Auth.AuthCookies.AccessTokenName,
                Description = "HttpOnly portal session cookie issued by sign-in. Browsers send it with credentials included." });
            options.AddSecurityDefinition("csrfToken", new OpenApiSecurityScheme {
                Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = "X-CSRF-Token",
                Description = "Obtain from /api/auth/csrf using the matching cookie session. The frontend transport supplies this header automatically." });
        });
        return services;
    }

    private static string SchemaId(Type type) => type.IsGenericType
        ? type.GetGenericTypeDefinition().Name.Split('`')[0] + "_" + string.Join("_", type.GetGenericArguments().Select(SchemaId))
        : (type.FullName ?? type.Name).Replace('+', '.');

    // This host registers metadata only: no application services, migrations, keys or jobs.
    public static async Task ExportAsync(string path)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            Args = [], EnvironmentName = "OpenApiExport", ApplicationName = typeof(BankingController).Assembly.FullName });
        builder.Services.AddControllers().AddApplicationPart(typeof(BankingController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddPortalOpenApi();
        await using var app = builder.Build();
        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        document.SerializeAsV3(new OpenApiJsonWriter(text));
        var json = JsonNode.Parse(text.ToString())!;
        var output = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, Sort(json).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Exported {document.Paths.Count} OpenAPI paths to {output}");
    }

    private static JsonNode Sort(JsonNode node) => node switch {
        JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => KeyValuePair.Create(x.Key, x.Value is null ? null : Sort(x.Value)))),
        JsonArray array => new JsonArray(array.Select(x => x is null ? null : Sort(x)).ToArray()),
        _ => node.DeepClone()
    };
}

public sealed class ContractRequiredProperties : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        // These response records serialize every property, including nullable values.
        var isBankingResponse = context.Type.Namespace == "SecureClientPortal.Backend.Application.Modules.Banking" &&
            (context.Type.Name.EndsWith("Dto", StringComparison.Ordinal) || context.Type.Name == "ManualTransactionImportResult");
        foreach (var property in context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!schema.Properties.ContainsKey(name)) continue;
            var constructorParameter = context.Type.GetConstructors().SelectMany(c => c.GetParameters())
                .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (isBankingResponse || context.Type.Namespace?.StartsWith("SecureClientPortal.Backend.Domain.", StringComparison.Ordinal) == true || context.Type.Namespace == "SecureClientPortal.Backend.Models" || (constructorParameter is { HasDefaultValue: false } &&
                (Nullable.GetUnderlyingType(property.PropertyType) is null && property.PropertyType.IsValueType ||
                 new NullabilityInfoContext().Create(property).ReadState == NullabilityState.NotNull)))
                schema.Required.Add(name);
        }
    }
}

public sealed class ContractSecurity : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        var authenticated = !metadata.OfType<IAllowAnonymous>().Any() && metadata.OfType<IAuthorizeData>().Any();
        var security = new OpenApiSecurityRequirement();
        if (authenticated)
            security.Add(new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "portalCookie" } }, []);
        if (context.ApiDescription.HttpMethod is not ("GET" or "HEAD" or "OPTIONS"))
            security.Add(new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "csrfToken" } }, []);
        if (security.Count > 0) operation.Security.Add(security);
        if (authenticated) operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Sign-in required." });
        if (security.Count > 0) operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Client access denied, role denied, or CSRF validation failed." });

        var body = context.ApiDescription.ParameterDescriptions.FirstOrDefault(p => p.Source == BindingSource.Body);
        var parameter = context.MethodInfo.GetParameters().FirstOrDefault(p => p.Name == body?.Name);
        if (operation.RequestBody is not null && parameter is { HasDefaultValue: false } &&
            new NullabilityInfoContext().Create(parameter).ReadState != NullabilityState.Nullable)
            operation.RequestBody.Required = true;

        if (context.MethodInfo.DeclaringType == typeof(BankingController) && operation.Responses.TryGetValue("400", out var badRequest))
        {
            // Automatic model validation, service validation and exception middleware
            // have different real response shapes; document all three.
            var error = context.SchemaGenerator.GenerateSchema(typeof(BankingErrorResponse), context.SchemaRepository);
            var validation = context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository);
            var problem = context.SchemaGenerator.GenerateSchema(typeof(PortalApiProblem), context.SchemaRepository);
            foreach (var media in badRequest.Content.Values)
                media.Schema = new OpenApiSchema { OneOf = [error, validation, problem] };
        }
    }
}

public sealed record PortalApiProblem(string Code, string Message, string TraceId);
