using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

var root = Path.GetFullPath(args[0]);
var sourceFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
    .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")).ToArray();
var trees = sourceFiles.Select(p => CSharpSyntaxTree.ParseText(File.ReadAllText(p), new CSharpParseOptions(LanguageVersion.Preview), p)).ToList();
trees.Add(CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks; global using Microsoft.AspNetCore.Builder; global using Microsoft.AspNetCore.Hosting; global using Microsoft.AspNetCore.Http; global using Microsoft.AspNetCore.Routing; global using Microsoft.Extensions.Configuration; global using Microsoft.Extensions.DependencyInjection; global using Microsoft.Extensions.Hosting; global using Microsoft.Extensions.Logging;", new CSharpParseOptions(LanguageVersion.Preview)));
var bin = Path.Combine(root, "src/SecureClientPortal.Api/bin/Release/net8.0");
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Concat(Directory.GetFiles(bin, "*.dll").Where(p => !Path.GetFileName(p).StartsWith("SecureClientPortal.")))
    .DistinctBy(Path.GetFileName).Select(p => MetadataReference.CreateFromFile(p));
var compilation = CSharpCompilation.Create("ContractSource", trees, references, new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
var engine = new Contracts(compilation, trees);
var actions = new SortedDictionary<string, object>(StringComparer.Ordinal);
var unknown = new List<string>();
foreach (var tree in trees.Where(t => t.FilePath.EndsWith("Controller.cs")))
{
    var model = compilation.GetSemanticModel(tree);
    foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
    {
        if (!method.AttributeLists.SelectMany(a => a.Attributes).Any(a => a.Name.ToString().StartsWith("Http"))) continue;
        if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol) continue;
        var key = $"{symbol.ContainingType.ToDisplayString()}|{symbol.Name}|{string.Join(",", symbol.Parameters.Select(p => p.Name))}";
        var expressions = method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        var delegates = expressions.Select(e => model.GetSymbolInfo(e).Symbol).OfType<IMethodSymbol>()
            .Where(m => m.GetAttributes().Any(a => a.AttributeClass?.Name == "NonActionAttribute"))
            .SelectMany(m => m.DeclaringSyntaxReferences.Select(r => r.GetSyntax()));
        expressions = expressions.Concat(delegates.SelectMany(n => n.DescendantNodes().OfType<InvocationExpressionSyntax>())).ToArray();
        var binary = expressions.Any(e => e.Expression.ToString() is "File" or "PhysicalFile");
        var types = engine.InferMethod(symbol, new HashSet<ISymbol>(SymbolEqualityComparer.Default)).Distinct<ITypeSymbol>(SymbolEqualityComparer.Default).ToArray();
        var schemas = types.Where(t => !engine.IsUnknown(t)).Select(engine.Schema).ToArray();
        var empty = expressions.Any(e => e.Expression.ToString() is "NoContent" || e.Expression.ToString() == "Ok" && e.ArgumentList.Arguments.Count == 0);
        if (schemas.Length == 0 && !binary && !empty) unknown.Add(key);
        var created = expressions.Any(e => e.Expression.ToString() is "Created" or "CreatedAtAction");
        var noContent = expressions.Any(e => e.Expression.ToString() == "NoContent");
        actions[key] = new { status = created ? 201 : noContent && schemas.Length == 0 ? 204 : 200,
            binary, empty = empty && schemas.Length == 0,
            schema = binary ? new Dictionary<string,object?>{{"type","string"},{"format","binary"}} : schemas.Length switch {0 => null, 1 => schemas[0], _ => new Dictionary<string, object?> { ["oneOf"] = schemas } } };
    }
}
var output = Path.Combine(root, "src/SecureClientPortal.Api/Configuration/SourceResponseContracts.json");
var contents = JsonSerializer.Serialize(new { actions, schemas = engine.Components }, new JsonSerializerOptions { WriteIndented = true }) + "\n";
if (unknown.Count > 0) throw new InvalidOperationException("Unresolved response shapes: " + string.Join("; ", unknown));
if (args.Contains("--check"))
{
    if (!File.Exists(output) || File.ReadAllText(output).Replace("\r\n", "\n") != contents.Replace("\r\n", "\n"))
        throw new InvalidOperationException("Source response metadata is stale. Regenerate tools/ContractMetadata and commit SourceResponseContracts.json.");
}
else File.WriteAllText(output, contents);
Console.WriteLine($"Inferred {actions.Count - unknown.Count}/{actions.Count} controller responses. Components: {engine.Components.Count}");
foreach (var item in unknown) Console.WriteLine("UNRESOLVED " + item);

sealed class Contracts(CSharpCompilation compilation, IEnumerable<SyntaxTree> trees)
{
    readonly List<INamedTypeSymbol> classes = trees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Select(n => compilation.GetSemanticModel(t).GetDeclaredSymbol(n))).OfType<INamedTypeSymbol>().ToList();
    public SortedDictionary<string, object> Components { get; } = new(StringComparer.Ordinal);
    static ITypeSymbol Unwrap(ITypeSymbol t) => t is INamedTypeSymbol n && n.TypeArguments.Length == 1 && n.Name is "Task" or "ValueTask" or "ServiceResult" or "BankingOperationResult" or "ActionResult" ? Unwrap(n.TypeArguments[0]) : t;
    public bool IsUnknown(ITypeSymbol t) => t.TypeKind is TypeKind.Error or TypeKind.TypeParameter || t.SpecialType == SpecialType.System_Object || t.Name is "IActionResult" or "ActionResult" or "Task" or "Void" ||
        t is INamedTypeSymbol n && (n.IsTupleType || n.AllInterfaces.Any(i => i.Name == "IActionResult"));
    bool HasUnknownElement(ITypeSymbol t) => t is INamedTypeSymbol n && n.TypeArguments.Any(x => IsUnknown(x));

    public IEnumerable<ITypeSymbol> InferMethod(IMethodSymbol method, HashSet<ISymbol> seen)
    {
        if (!seen.Add(method.OriginalDefinition) || seen.Count > 50) return [];
        var type = Unwrap(method.ReturnType);
        if (!IsUnknown(type) && !HasUnknownElement(type)) return [type];
        if (method.ContainingType.TypeKind == TypeKind.Interface)
        {
            return classes.Select(c => c.FindImplementationForInterfaceMember(method)).OfType<IMethodSymbol>()
                .SelectMany(m => InferMethod(m, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default))).ToArray();
        }
        var result = new List<ITypeSymbol>();
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            var model = compilation.GetSemanticModel(node.SyntaxTree);
            var success = node.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => {
                var name = i.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : i.Expression.ToString();
                return name is "Ok" or "Created" or "CreatedAtAction" or "Success" or "FromResult" or "Result";
            }).ToArray();
            foreach (var invocation in success)
            {
                var name = invocation.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : invocation.Expression.ToString();
                if (invocation.ArgumentList.Arguments.Count == 0) continue;
                if (name == "FromResult" && invocation.ArgumentList.Arguments.Count > 1 &&
                    model.GetSymbolInfo(invocation.ArgumentList.Arguments.Last().Expression).Symbol is IMethodSymbol mapper)
                {
                    result.AddRange(InferMethod(mapper, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)));
                    continue;
                }
                var argument = name is "Created" or "CreatedAtAction" ? invocation.ArgumentList.Arguments.Last().Expression : invocation.ArgumentList.Arguments[0].Expression;
                result.AddRange(InferExpression(argument, model, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)));
            }
            foreach (var call in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
                if (model.GetSymbolInfo(call).Symbol is IMethodSymbol delegated && delegated.GetAttributes().Any(a => a.AttributeClass?.Name == "NonActionAttribute"))
                    result.AddRange(InferMethod(delegated, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)));
            // Also follow delegated returns after early empty-success branches.
            {
                foreach (var ret in node.DescendantNodes().OfType<ReturnStatementSyntax>().Where(r => r.Expression is not null &&
                    !r.Ancestors().TakeWhile(a => a != node).Any(a => a is AnonymousFunctionExpressionSyntax)))
                    result.AddRange(InferExpression(ret.Expression!, model, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)));
                if (node is MethodDeclarationSyntax { ExpressionBody: not null } expressionMethod)
                    result.AddRange(InferExpression(expressionMethod.ExpressionBody.Expression, model, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)));
            }
        }
        return result.Where(t => !IsUnknown(t)).ToArray();
    }

    IEnumerable<ITypeSymbol> InferExpression(ExpressionSyntax expression, SemanticModel model, HashSet<ISymbol> seen)
    {
        if (expression.IsKind(SyntaxKind.NullLiteralExpression)) return [];
        if (expression is CastExpressionSyntax cast) return InferExpression(cast.Expression, model, seen);
        if (expression is InvocationExpressionSyntax factory)
        {
            var name = factory.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : factory.Expression.ToString();
            if (name is "ErrorResult" or "ForbiddenResult" or "NotFoundResult" or "UnauthorizedResult" or "Fail" or "Denied" or "BadRequest" or "NotFound" or "Unauthorized" or "Forbid") return [];
            if (name == "Success" && factory.ArgumentList.Arguments.Count > 0)
                return InferExpression(factory.ArgumentList.Arguments[0].Expression, model, seen);
        }
        var type = model.GetTypeInfo(expression).Type;
        if (type is not null)
        {
            var unwrapped = Unwrap(type);
            if (!IsUnknown(unwrapped) && !HasUnknownElement(unwrapped)) return [unwrapped];
        }
        if (expression is AwaitExpressionSyntax awaited) return InferExpression(awaited.Expression, model, seen);
        if (expression is InvocationExpressionSyntax invocation && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol called)
        {
            var inferred = InferMethod(called, seen).ToArray();
            if (inferred.Length > 0) return inferred;
            if (type is INamedTypeSymbol container && container.TypeArguments.Length == 1)
            {
                var projectionCall = invocation.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault(i => i.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.Text == "Select");
                if (projectionCall?.ArgumentList.Arguments.LastOrDefault()?.Expression is SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body })
                {
                    var elementTypes = InferExpression(body, model, seen).ToArray();
                    if (elementTypes.Length > 0) return elementTypes.Select(t => compilation.CreateArrayTypeSymbol(t)).ToArray();
                }
                var projection = invocation.DescendantNodes().OfType<AnonymousObjectCreationExpressionSyntax>().FirstOrDefault();
                if (projection is not null && model.GetTypeInfo(projection).Type is ITypeSymbol element) return [compilation.CreateArrayTypeSymbol(element)];
            }
        }
        if (expression is TupleExpressionSyntax tuple)
            return tuple.Arguments.Where(a => !a.Expression.IsKind(SyntaxKind.TrueLiteralExpression) && !a.Expression.IsKind(SyntaxKind.FalseLiteralExpression))
                .SelectMany(a => InferExpression(a.Expression, model, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default))).ToArray();
        if (expression is IdentifierNameSyntax id && model.GetSymbolInfo(id).Symbol is ILocalSymbol local)
        {
            if (HasUnknownElement(local.Type))
            {
                var methodBody = id.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var additions = methodBody?.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i =>
                    i.Expression is MemberAccessExpressionSyntax m && m.Expression.ToString() == id.Identifier.Text && m.Name.Identifier.Text == "Add");
                var elementTypes = additions?.SelectMany(i => i.ArgumentList.Arguments.SelectMany(a => InferExpression(a.Expression, model,
                    new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)))).ToArray();
                if (elementTypes?.Length > 0) return elementTypes.Select(t => compilation.CreateArrayTypeSymbol(t)).ToArray();
            }
            var declaration = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
            if (declaration?.Initializer is not null) return InferExpression(declaration.Initializer.Value, model, seen);
        }
        if (expression is MemberAccessExpressionSyntax access && access.Expression is IdentifierNameSyntax variable && model.GetSymbolInfo(variable).Symbol is ILocalSymbol tupleLocal)
        {
            var declaration = tupleLocal.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
            if (declaration?.Initializer is not null)
            {
                var initializer = declaration.Initializer.Value is AwaitExpressionSyntax a ? a.Expression : declaration.Initializer.Value;
                if (tupleLocal.Type is INamedTypeSymbol { IsTupleType: true } tupleType && initializer is InvocationExpressionSyntax call &&
                    model.GetSymbolInfo(call).Symbol is IMethodSymbol tupleMethod)
                {
                    var position = tupleType.TupleElements.IndexOf(tupleType.TupleElements.First(p => p.Name == access.Name.Identifier.Text));
                    var values = new List<ITypeSymbol>();
                    foreach (var source in tupleMethod.DeclaringSyntaxReferences)
                    {
                        var syntax = source.GetSyntax(); var semantic = compilation.GetSemanticModel(syntax.SyntaxTree);
                        foreach (var ret in syntax.DescendantNodes().OfType<ReturnStatementSyntax>())
                            if (ret.Expression is TupleExpressionSyntax returned && returned.Arguments.Count > position)
                                values.AddRange(InferExpression(returned.Arguments[position].Expression, semantic, new HashSet<ISymbol>(seen, SymbolEqualityComparer.Default)));
                    }
                    if (values.Count > 0) return values;
                }
                return InferExpression(declaration.Initializer.Value, model, seen);
            }
        }
        return [];
    }

    public Dictionary<string,object?> Schema(ITypeSymbol t)
    {
        if (t is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        { var value = Schema(nullable.TypeArguments[0]); value["nullable"] = true; return value; }
        Dictionary<string,object?> schema;
        if (t is IArrayTypeSymbol array) schema = new() { ["type"] = "array", ["items"] = Schema(array.ElementType) };
        else if (t.SpecialType == SpecialType.System_String || t.SpecialType == SpecialType.System_Char) schema = new() { ["type"] = "string" };
        else if (t.SpecialType == SpecialType.System_Boolean) schema = new() { ["type"] = "boolean" };
        else if (t.SpecialType is SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_UInt32 or SpecialType.System_UInt64) schema = new() { ["type"] = "integer" };
        else if (t.SpecialType is SpecialType.System_Decimal or SpecialType.System_Double or SpecialType.System_Single) schema = new() { ["type"] = "number" };
        else if (t.Name is "Guid" or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly") schema = new() { ["type"] = "string", ["format"] = t.Name == "Guid" ? "uuid" : t.Name == "DateOnly" ? "date" : t.Name == "TimeOnly" ? "time" : "date-time" };
        else if (t is INamedTypeSymbol dictionary && dictionary.TypeArguments.Length == 2 && (dictionary.Name.Contains("Dictionary") || dictionary.AllInterfaces.Any(i => i.Name == "IDictionary")))
            schema = new() { ["type"] = "object", ["additionalProperties"] = Schema(dictionary.TypeArguments[1]) };
        else if (t is INamedTypeSymbol sequence && sequence.SpecialType != SpecialType.System_String && (sequence.Name == "IEnumerable" || sequence.AllInterfaces.Any(i => i.Name == "IEnumerable" && i.TypeArguments.Length == 1)))
        {
            var element = sequence.Name == "IEnumerable" ? sequence.TypeArguments.FirstOrDefault() : sequence.AllInterfaces.First(i => i.Name == "IEnumerable" && i.TypeArguments.Length == 1).TypeArguments[0];
            schema = new() { ["type"] = "array", ["items"] = element is null ? new Dictionary<string,object?>() : Schema(element) };
        }
        else if (t.TypeKind == TypeKind.Enum) schema = new() { ["type"] = "integer", ["enum"] = t.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).Select(f => f.ConstantValue).ToArray() };
        else if (IsUnknown(t) || t.Name is "JsonElement" or "JsonDocument" or "JsonNode") schema = new();
        else
        {
            var label = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
            if (t.IsAnonymousType) label = "Anonymous_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)))[..16];
            var key = "Source." + label;
            schema = new() { ["$ref"] = "#/components/schemas/" + key };
            if (!Components.ContainsKey(key))
            {
                Components[key] = new Dictionary<string,object?>();
                var properties = new SortedDictionary<string,object?>(StringComparer.Ordinal);
                for (var current = t as INamedTypeSymbol; current is not null; current = current.BaseType)
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>().Where(p => p.GetMethod?.DeclaredAccessibility == Accessibility.Public && !p.IsStatic && !p.IsIndexer))
                {
                    if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonIgnoreAttribute")) continue;
                    var renamed = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "JsonPropertyNameAttribute")?.ConstructorArguments.FirstOrDefault().Value as string;
                    var name = renamed ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                    properties.TryAdd(name, Schema(property.Type));
                }
                Components[key] = new Dictionary<string,object?> { ["type"] = "object", ["properties"] = properties, ["required"] = properties.Keys.ToArray() };
            }
        }
        if (t.NullableAnnotation == NullableAnnotation.Annotated) schema["nullable"] = true;
        return schema;
    }
}
