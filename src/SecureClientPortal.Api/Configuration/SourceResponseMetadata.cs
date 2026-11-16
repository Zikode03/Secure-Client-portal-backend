using System.Text.Json;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SecureClientPortal.Backend.Api.Configuration;

/// <summary>Response shapes inferred from actual return expressions by tools/ContractMetadata.</summary>
public sealed class SourceResponseMetadata : IOperationFilter, IDocumentFilter
{
    private static readonly Lazy<JsonDocument> Data = new(() => {
        var assembly = typeof(SourceResponseMetadata).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".SourceResponseContracts.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        return JsonDocument.Parse(stream);
    });

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.MethodInfo;
        var key = $"{method.DeclaringType!.FullName}|{method.Name}|{string.Join(",", method.GetParameters().Select(p => p.Name))}";
        if (!Data.Value.RootElement.GetProperty("actions").TryGetProperty(key, out var metadata))
            throw new InvalidOperationException($"No response contract for {key}. Regenerate source contracts.");
        if (context.MethodInfo.GetCustomAttributes(typeof(SecureClientPortal.Backend.Api.Security.StreamingMultipartAttribute), true).Length > 0)
        {
            var form = method.DeclaringType.GetMethods().Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.NonActionAttribute), true).Length > 0)
                .SelectMany(m => m.GetParameters()).FirstOrDefault(p => p.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.FromFormAttribute), true).Length > 0);
            if (form is not null) operation.RequestBody = new OpenApiRequestBody { Required = true,
                Content = { ["multipart/form-data"] = new OpenApiMediaType { Schema = context.SchemaGenerator.GenerateSchema(form.ParameterType, context.SchemaRepository) } } };
        }
        if (operation.RequestBody?.Content.TryGetValue("multipart/form-data", out var multipart) == true)
        {
            var formSchema = multipart.Schema.Reference is null ? multipart.Schema : context.SchemaRepository.Schemas[multipart.Schema.Reference.Id];
            foreach (var property in formSchema.Properties.Where(p => p.Value.Format == "binary")) formSchema.Required.Add(property.Key);
            if (method.DeclaringType.Name == "DocumentsController")
                foreach (var name in new[] { "clientId", "documentType", "file" }) formSchema.Required.Add(name);
            operation.RequestBody.Required = true;
        }
        if (operation.Responses.Any(r => r.Key.StartsWith('2') && r.Value.Content.Values.Any(m => m.Schema is not null))) return;
        var status = metadata.GetProperty("status").GetInt32().ToString();
        if (metadata.GetProperty("empty").GetBoolean())
        {
            foreach (var response in operation.Responses.Keys.Where(k => k.StartsWith('2')).ToArray()) operation.Responses.Remove(response);
            operation.Responses[status] = new OpenApiResponse { Description = "Successful response with no body.", Extensions = { ["x-empty-body"] = new OpenApiBoolean(true) } };
            return;
        }
        var raw = metadata.GetProperty("schema");
        if (raw.ValueKind == JsonValueKind.Null) throw new InvalidOperationException($"Unresolved response contract for {key}.");
        operation.Responses[status] = new OpenApiResponse {
            Description = status == "201" ? "Created" : "OK",
            Content = { [metadata.GetProperty("binary").GetBoolean() ? "application/octet-stream" : "application/json"] = new OpenApiMediaType { Schema = ParseSchema(raw) } }
        };
        if (status != "200" && operation.Responses.TryGetValue("200", out var existing) && existing.Content.Count == 0) operation.Responses.Remove("200");
    }

    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        foreach (var item in Data.Value.RootElement.GetProperty("schemas").EnumerateObject())
            document.Components.Schemas.TryAdd(item.Name, ParseSchema(item.Value));
    }

    private static OpenApiSchema ParseSchema(JsonElement raw)
    {
        var schema = new OpenApiSchema();
        if (raw.TryGetProperty("$ref", out var reference)) schema.Reference = new OpenApiReference { Type = ReferenceType.Schema, Id = reference.GetString()!["#/components/schemas/".Length..] };
        if (raw.TryGetProperty("type", out var type)) schema.Type = type.GetString();
        if (raw.TryGetProperty("format", out var format)) schema.Format = format.GetString();
        if (raw.TryGetProperty("nullable", out var nullable)) schema.Nullable = nullable.GetBoolean();
        if (raw.TryGetProperty("properties", out var properties)) foreach (var p in properties.EnumerateObject()) schema.Properties[p.Name] = ParseSchema(p.Value);
        if (raw.TryGetProperty("required", out var required)) foreach (var r in required.EnumerateArray()) schema.Required.Add(r.GetString()!);
        if (raw.TryGetProperty("items", out var items)) schema.Items = ParseSchema(items);
        if (raw.TryGetProperty("additionalProperties", out var additional)) schema.AdditionalProperties = ParseSchema(additional);
        if (raw.TryGetProperty("oneOf", out var choices)) foreach (var c in choices.EnumerateArray()) schema.OneOf.Add(ParseSchema(c));
        if (raw.TryGetProperty("enum", out var values)) foreach (var v in values.EnumerateArray()) schema.Enum.Add(v.ValueKind == JsonValueKind.String ? new OpenApiString(v.GetString()) : new OpenApiInteger(v.GetInt32()));
        if (schema.Reference is not null && schema.Nullable)
            return new OpenApiSchema { Nullable = true, AllOf = [new OpenApiSchema { Reference = schema.Reference }] };
        return schema;
    }
}
