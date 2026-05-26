using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace HeroParser.Generators;

/// <summary>
/// Statically generates LLM-compatible JSON Schema representations for HeroParser records at compile time.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LlmSchemaGenerator : IIncrementalGenerator
{
    private const string GENERATE_ATTRIBUTE = "HeroParser.GenerateBinderAttribute";

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GENERATE_ATTRIBUTE,
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, ct) => Transform(ctx, ct))
            .Where(static x => x is not null);

        context.RegisterSourceOutput(provider, static (spc, descriptor) => EmitSchema(spc, descriptor!));
    }

    private static SchemaDescriptor? Transform(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        if (symbol.IsAbstract || !GeneratorHelpers.IsTypeAccessible(symbol))
            return null;

        var fullyQualifiedName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var properties = new List<PropertySchemaDescriptor>();
        var requiredProperties = new List<string>();

        foreach (var member in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public || member.IsWriteOnly)
                continue;

            var headerName = member.Name;
            var tabularAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "HeroParser.TabularMapAttribute");
            if (tabularAttr != null)
            {
                var nameVal = tabularAttr.NamedArguments.FirstOrDefault(kv => kv.Key == "Name").Value.Value as string;
                if (!string.IsNullOrWhiteSpace(nameVal))
                {
                    headerName = nameVal!;
                }
            }

            var jsonType = member.Type.SpecialType switch
            {
                SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16 or SpecialType.System_Byte or SpecialType.System_UInt32 or SpecialType.System_UInt64 or SpecialType.System_UInt16 or SpecialType.System_SByte => "integer",
                SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal => "number",
                SpecialType.System_Boolean => "boolean",
                _ => "string"
            };

            double? rangeMin = null;
            double? rangeMax = null;
            string? pattern = null;
            bool isRequired = false;

            var validateAttr = member.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "HeroParser.ValidateAttribute");
            if (validateAttr != null)
            {
                foreach (var arg in validateAttr.NamedArguments)
                {
                    if (arg.Key == "NotNull" && arg.Value.Value is bool bn && bn) isRequired = true;
                    if (arg.Key == "NotEmpty" && arg.Value.Value is bool bne && bne) isRequired = true;
                    if (arg.Key == "RangeMin" && arg.Value.Value is double rm && !double.IsNaN(rm)) rangeMin = rm;
                    if (arg.Key == "RangeMax" && arg.Value.Value is double rx && !double.IsNaN(rx)) rangeMax = rx;
                    if (arg.Key == "Pattern" && arg.Value.Value is string p) pattern = p;
                }
            }

            if (isRequired)
            {
                requiredProperties.Add(member.Name);
            }

            properties.Add(new PropertySchemaDescriptor(
                member.Name,
                headerName,
                jsonType,
                rangeMin,
                rangeMax,
                pattern
            ));
        }

        return new SchemaDescriptor(
            symbol,
            fullyQualifiedName,
            properties,
            requiredProperties
        );
    }

    private static void EmitSchema(SourceProductionContext context, SchemaDescriptor descriptor)
    {
        var jsonSb = new StringBuilder();
        jsonSb.Append("{");
        jsonSb.Append("\\\"type\\\": \\\"object\\\",");
        jsonSb.Append("\\\"properties\\\": {");

        for (int i = 0; i < descriptor.Properties.Count; i++)
        {
            var prop = descriptor.Properties[i];
            jsonSb.Append($"\\\"{prop.PropertyName}\\\": {{");
            jsonSb.Append($"\\\"type\\\": \\\"{prop.JsonType}\\\",");
            jsonSb.Append($"\\\"description\\\": \\\"Mapped from column '{GeneratorHelpers.EscapeString(prop.HeaderName)}'\\\"");

            if (prop.RangeMin.HasValue)
            {
                jsonSb.Append($",\\\"minimum\\\": {prop.RangeMin.Value}");
            }
            if (prop.RangeMax.HasValue)
            {
                jsonSb.Append($",\\\"maximum\\\": {prop.RangeMax.Value}");
            }
            if (prop.Pattern != null)
            {
                jsonSb.Append($",\\\"pattern\\\": \\\"{GeneratorHelpers.EscapeString(prop.Pattern)}\\\"");
            }

            jsonSb.Append("}");
            if (i < descriptor.Properties.Count - 1)
            {
                jsonSb.Append(",");
            }
        }

        jsonSb.Append("}");

        if (descriptor.RequiredProperties.Count > 0)
        {
            jsonSb.Append(",\\\"required\\\": [");
            for (int i = 0; i < descriptor.RequiredProperties.Count; i++)
            {
                jsonSb.Append($"\\\"{descriptor.RequiredProperties[i]}\\\"");
                if (i < descriptor.RequiredProperties.Count - 1)
                {
                    jsonSb.Append(",");
                }
            }
            jsonSb.Append("]");
        }

        jsonSb.Append("}");

        var preRenderedJson = jsonSb.ToString();

        var safeClassName = GeneratorHelpers.CreateSafeClassName(descriptor.Symbol);

        var code = new StringBuilder();
        code.AppendLine("// <auto-generated/>");
        code.AppendLine("#nullable enable");
        code.AppendLine("using System;");
        code.AppendLine("using System.Runtime.CompilerServices;");
        code.AppendLine("using HeroParser.AI;");
        code.AppendLine();
        code.AppendLine("namespace HeroParser.Generators.Generated;");
        code.AppendLine();
        code.AppendLine($"file static class LlmSchemaRegistration_{safeClassName}");
        code.AppendLine("{");
        code.AppendLine("    [ModuleInitializer]");
        code.AppendLine("    internal static void Register()");
        code.AppendLine("    {");
        code.AppendLine($"        SchemaMetadata.RegisterSchema<{descriptor.FullyQualifiedName}>(\"{preRenderedJson}\");");
        code.AppendLine("    }");
        code.AppendLine("}");

        context.AddSource($"LlmSchema.{safeClassName}.g.cs", code.ToString());
    }

    private sealed class SchemaDescriptor
    {
        public INamedTypeSymbol Symbol { get; }
        public string FullyQualifiedName { get; }
        public List<PropertySchemaDescriptor> Properties { get; }
        public List<string> RequiredProperties { get; }

        public SchemaDescriptor(
            INamedTypeSymbol symbol,
            string fullyQualifiedName,
            List<PropertySchemaDescriptor> properties,
            List<string> requiredProperties)
        {
            Symbol = symbol;
            FullyQualifiedName = fullyQualifiedName;
            Properties = properties;
            RequiredProperties = requiredProperties;
        }
    }

    private sealed class PropertySchemaDescriptor
    {
        public string PropertyName { get; }
        public string HeaderName { get; }
        public string JsonType { get; }
        public double? RangeMin { get; }
        public double? RangeMax { get; }
        public string? Pattern { get; }

        public PropertySchemaDescriptor(
            string propertyName,
            string headerName,
            string jsonType,
            double? rangeMin,
            double? rangeMax,
            string? pattern)
        {
            PropertyName = propertyName;
            HeaderName = headerName;
            JsonType = jsonType;
            RangeMin = rangeMin;
            RangeMax = rangeMax;
            Pattern = pattern;
        }
    }
}
