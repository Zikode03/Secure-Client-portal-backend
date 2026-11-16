using System.Text.Json.Nodes;
using SecureClientPortal.Backend.Api.Configuration;

namespace SecureClientPortal.Backend.Tests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task Export_IsDeterministicAndIncludesTypedSecuredBankingWithoutDatabaseStartup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"portal-openapi-{Guid.NewGuid():N}.json");
        try
        {
            await OpenApiContract.ExportAsync(path);
            var first = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            await OpenApiContract.ExportAsync(path);
            Assert.Equal(first, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            var document = JsonNode.Parse(first)!;
            var paths = document["paths"]!.AsObject();
            Assert.True(paths.Count > 100);
            Assert.DoesNotContain("Source.Microsoft.AspNetCore.Mvc", first);
            foreach (var route in paths)
            foreach (var operation in route.Value!.AsObject().Where(x => x.Key is "get" or "post" or "put" or "patch" or "delete"))
            {
                var successes = operation.Value!["responses"]!.AsObject().Where(x => x.Key.StartsWith('2')).ToArray();
                Assert.Contains(successes, response => response.Value!["x-empty-body"]?.GetValue<bool>() == true ||
                    response.Value!["content"] is JsonObject content && content.Any(media => media.Value?["schema"] is not null));
            }
            var form = paths["/api/document-slots/{slotId}/upload"]!["post"]!["requestBody"]!;
            Assert.True(form["required"]!.GetValue<bool>());
            Assert.Contains(form["content"]!["multipart/form-data"]!["schema"]!["required"]!.AsArray(), x => x!.GetValue<string>() == "File");

            var import = paths["/api/banking/manual/import"]!["post"]!;
            Assert.NotNull(import["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]);
            Assert.True(import["requestBody"]!["required"]!.GetValue<bool>());
            Assert.NotNull(import["security"]![0]!["portalCookie"]);
            Assert.NotNull(import["security"]![0]!["csrfToken"]);
            Assert.Equal(3, import["responses"]!["400"]!["content"]!["application/json"]!["schema"]!["oneOf"]!.AsArray().Count);
            Assert.All(paths["/api/banking/monthly-pack-status"]!["get"]!["parameters"]!.AsArray(),
                p => Assert.True(p!["required"]!.GetValue<bool>()));
            Assert.NotNull(paths["/api/auth/csrf"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]);
            Assert.NotNull(paths["/api/auth/login"]!["post"]!["security"]![0]!["csrfToken"]);
            var account = document["components"]!["schemas"]!["SecureClientPortal.Backend.Application.Modules.Banking.BankAccountDto"]!;
            Assert.Contains(account["required"]!.AsArray(), x => x!.GetValue<string>() == "currentBalance");
            Assert.True(account["properties"]!["currentBalance"]!["nullable"]!.GetValue<bool>());
            var operations = paths.SelectMany(p => p.Value!.AsObject()).Where(p => p.Key is "get" or "post" or "put" or "patch" or "delete")
                .Select(p => p.Value!["operationId"]!.GetValue<string>()).ToArray();
            Assert.Equal(operations.Length, operations.Distinct().Count());
        }
        finally { File.Delete(path); }
    }
}
