using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace R3.JsonConfig.Generators;

[Generator]
public class DefaultJsonDtoGenerator : IIncrementalGenerator {
	protected virtual string TargetAttribute {
		get;
	} = "R3.JsonConfig.Attributes.GenerateR3JsonConfigDtoAttribute";

	private enum PropertyKind {
		Plain,
		ReactiveProperty,
		ObservableList
	}

	private enum TypeKind {
		Plain,
		ForJson
	}

	public void Initialize(IncrementalGeneratorInitializationContext context) {
		var candidates = context.SyntaxProvider
			.CreateSyntaxProvider(static (s, _) => s is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
				(ctx, _) => this.GetTarget(ctx))
			.Where(static m => m is not null);

		var compilationAndModels = context.CompilationProvider.Combine(candidates.Collect());
		context.RegisterSourceOutput(compilationAndModels, (spc, source) => this.Execute(spc, source.Left, source.Right!));
	}

	private INamedTypeSymbol? GetTarget(GeneratorSyntaxContext ctx) {
		if (ctx.Node is not ClassDeclarationSyntax cds) {
			return null;
		}
		if (ctx.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) {
			return null;
		}
		return this.HasGenerateJsonDtoAttribute(symbol) ? symbol : null;
	}

	private void Execute(SourceProductionContext context, Compilation _, IEnumerable<INamedTypeSymbol> symbols) {
		foreach (var symbol in symbols) {
			try {
				this.GenerateForSymbol(context, symbol);
			} catch (Exception ex) {
				context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
					"RJG001", "JsonDtoGenerator Error", "{0}", "JsonDtoGenerator", DiagnosticSeverity.Warning, true), Location.None, ex.Message));
			}
		}
	}

	private bool HasGenerateJsonDtoAttribute(INamedTypeSymbol symbol) {
		foreach (var attr in symbol.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() == this.TargetAttribute) {
				return true;
			}
		}
		return false;
	}

	private void GenerateForSymbol(SourceProductionContext context, INamedTypeSymbol modelSymbol) {
		var modelName = modelSymbol.Name;
		var dtoName = modelName + "ForJson";

		var props = new List<(string Name, string JsonType, PropertyKind PropertyKind, TypeKind TypeKind, string JsonItemType, string NonNullableItemTypeFullName)>();
		foreach (var member in modelSymbol.GetMembers().OfType<IPropertySymbol>()) {
			if (member.DeclaredAccessibility != Accessibility.Public) {
				continue;
			}
			var typeSymbol = member.Type;
			var typeName = typeSymbol.ToDisplayString();

			if (typeSymbol is INamedTypeSymbol nts && nts.TypeArguments.Length == 1 && nts.MetadataName == "ObservableList`1" && nts.ContainingNamespace.ToDisplayString() == "ObservableCollections") {
				var itemType = nts.TypeArguments[0];
				var display = itemType.ToDisplayString();
				var nonNullable = display.EndsWith("?") ? display.Substring(0, display.Length - 1) : display;

				if (itemType is INamedTypeSymbol itemNamed && this.HasGenerateJsonDtoAttribute(itemNamed)) {
					var itemDtoName = itemNamed.ToDisplayString() + "ForJson";
					props.Add((member.Name, $"{itemDtoName}[]?", PropertyKind.ObservableList, TypeKind.ForJson, itemDtoName, nonNullable));
					continue;
				}

				props.Add((member.Name, $"{itemType.ToDisplayString()}[]?", PropertyKind.ObservableList, TypeKind.Plain, itemType.ToDisplayString(), nonNullable));
				continue;
			}

			if (typeSymbol is INamedTypeSymbol reactive && reactive.TypeArguments.Length == 1 && reactive.MetadataName == "ReactiveProperty`1") {
				var innerTypeSymbol = reactive.TypeArguments[0];
				var innerDisplay = innerTypeSymbol.ToDisplayString();
				var innerNonNullable = innerDisplay.EndsWith("?") ? innerDisplay.Substring(0, innerDisplay.Length - 1) : innerDisplay;

				if (innerTypeSymbol is INamedTypeSymbol named && this.HasGenerateJsonDtoAttribute(named)) {
					var memberDtoName = innerNonNullable + "ForJson";
					props.Add((member.Name, memberDtoName + "?", PropertyKind.ReactiveProperty, TypeKind.ForJson, memberDtoName, innerNonNullable));
					continue;
				}
				props.Add((member.Name, innerNonNullable + "?", PropertyKind.ReactiveProperty, TypeKind.Plain, innerDisplay, innerNonNullable));
				continue;
			}

			var settableProperty = member.SetMethod is { } set && set.DeclaredAccessibility == Accessibility.Public;
			if (settableProperty) {
				var nonNullable = typeName.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;

				if (typeSymbol is INamedTypeSymbol named && this.HasGenerateJsonDtoAttribute(named)) {
					var memberDtoName = nonNullable + "ForJson";
					props.Add((member.Name, memberDtoName + "?", PropertyKind.Plain, TypeKind.ForJson, memberDtoName, nonNullable));
					continue;
				}
				props.Add((member.Name, nonNullable + "?", PropertyKind.Plain, TypeKind.Plain, nonNullable, nonNullable));
				continue;
			}
		}

		var propLinesBuilder = new StringBuilder();
		foreach (var p in props) {
			propLinesBuilder.AppendLine($$"""
	public {{p.JsonType}} {{p.Name}} {
		get;
		set;
	}

""");

		}
		var propLines = propLinesBuilder.ToString();

		var createModelBodyBuilder = new StringBuilder();
		foreach (var p in props) {
			var varName = "notNull" + p.Name;
			var modelValue = p.TypeKind switch {
				TypeKind.ForJson => $"{p.JsonItemType}.CreateModel(e,  sp.CreateScope().ServiceProvider)",
				TypeKind.Plain => $"e",
				_ => throw new Exception("Unknown type kind: " + p.TypeKind)
			};

			var setPropertyLogic = p.PropertyKind switch {
				PropertyKind.ReactiveProperty => $$"""
		if (json.{{p.Name}} is { } {{varName}}){
			var e = {{varName}};
			model.{{p.Name}}.Value = {{modelValue}};
		}
""",
				PropertyKind.ObservableList => $$"""
		if (json.{{p.Name}} is { } {{varName}}) {
			model.{{p.Name}}.Clear();
			foreach (var e in {{varName}}) {
				model.{{p.Name}}.Add({{modelValue}});
			}
		}
""",
				PropertyKind.Plain => $$"""
		if (json.{{p.Name}} is { } {{varName}}){
			var e = {{varName}};
			model.{{p.Name}} = {{modelValue}};
		}
""",
				_ => throw new Exception("Unknown property kind: " + p.TypeKind)
			};

			createModelBodyBuilder.AppendLine(setPropertyLogic);
		}


		var createModelBody = createModelBodyBuilder.ToString();

		var createJsonLinesBuilder = new StringBuilder();
		for (var i = 0; i < props.Count; i++) {
			var p = props[i];
			string setJsonPropertyLogic;
			switch (p.PropertyKind) {
				case PropertyKind.Plain:
					switch (p.TypeKind) {
						case TypeKind.Plain:
							setJsonPropertyLogic = $"model.{p.Name}";
							break;
						case TypeKind.ForJson:
							setJsonPropertyLogic = $"{p.JsonItemType}.CreateJson(model.{p.Name})";
							break;
						default:
							throw new Exception("Unknown type kind:" + p.TypeKind);
					}
					break;
				case PropertyKind.ReactiveProperty:
					switch (p.TypeKind) {
						case TypeKind.Plain:
							setJsonPropertyLogic = $"model.{p.Name}.Value";
							break;
						case TypeKind.ForJson:
							setJsonPropertyLogic = $"{p.JsonItemType}.CreateJson(model.{p.Name}.Value)";
							break;
						default:
							throw new Exception("Unknown type kind:" + p.TypeKind);
					}
					break;
				case PropertyKind.ObservableList:
					switch (p.TypeKind) {
						case TypeKind.Plain:
							setJsonPropertyLogic = $"model.{p.Name}.ToArray()";
							break;
						case TypeKind.ForJson:
							setJsonPropertyLogic = $"model.{p.Name}.Select(x => {p.JsonItemType}.CreateJson(x)).ToArray()";
							break;
						default:
							throw new Exception("Unknown type kind:" + p.TypeKind);
					}
					break;
				default:
					throw new Exception("Unknown property property");
			}

			createJsonLinesBuilder.AppendLine($"\t\t\t{p.Name} = {setJsonPropertyLogic},");
		}
		var createJsonLines = createJsonLinesBuilder.ToString();

		var ns = modelSymbol.ContainingNamespace.IsGlobalNamespace ? "" : modelSymbol.ContainingNamespace.ToDisplayString();
		var namespaceLine = string.IsNullOrWhiteSpace(ns) ? string.Empty : $"namespace {ns};";
		var full = $$"""
// <auto-generated />
#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

{{namespaceLine}}
public partial class {{dtoName}} {
{{propLines}}

	[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]
	public static {{modelName}}? CreateModel({{dtoName}}? json, System.IServiceProvider sp) {
		if(json is null){
			return null;
		}
		var model = sp.GetRequiredService<{{modelName}}>();
{{createModelBody}}
		return model;
	}

	[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]
	public static {{dtoName}}? CreateJson({{modelName}}? model) {
		if (model is null){
			return null;
		}

		return new() {
{{createJsonLines}}
		};
	}
}
""";
		context.AddSource(dtoName + ".g.cs", SourceText.From(full, Encoding.UTF8));
	}
}