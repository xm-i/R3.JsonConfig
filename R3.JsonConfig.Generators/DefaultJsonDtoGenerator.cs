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

	protected virtual string ExcludePropertyAttributeName {
		get;
	} = "R3.JsonConfig.Attributes.ExcludePropertyAttribute";

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
			.CreateSyntaxProvider(static (s, _) => s is ClassDeclarationSyntax {
				AttributeLists.Count: > 0
			},
				(ctx, _) => this.GetTarget(ctx))
			.Where(static m => m is { });

		var compilationAndModels = context.CompilationProvider.Combine(candidates.Collect());
		context.RegisterSourceOutput(compilationAndModels, (spc, source) => this.Execute(spc, source.Left, source.Right));
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
				context.ReportDiagnostic(Diagnostic.Create(new("RJG001", "JsonDtoGenerator Error", "{0}", "JsonDtoGenerator", DiagnosticSeverity.Warning, true), Location.None, ex.Message));
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

			// Skip properties with IgnorePropertyAttribute
			if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == this.ExcludePropertyAttributeName)) {
				continue;
			}
			var typeSymbol = member.Type;
			var typeName = typeSymbol.ToDisplayString();

			switch (typeSymbol) {
				case INamedTypeSymbol {
					TypeArguments.Length: 1, MetadataName: "ObservableList`1"
				} nts when nts.ContainingNamespace.ToDisplayString() == "ObservableCollections": {
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
				case INamedTypeSymbol {
					TypeArguments.Length: 1, MetadataName: "ReactiveProperty`1"
				} reactive: {
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
			}

			var settableProperty = member.SetMethod is {
				DeclaredAccessibility: Accessibility.Public
			};
			if (!settableProperty) {
				continue;
			}
			{
				var nonNullable = typeName.EndsWith("?") ? typeName.Substring(0, typeName.Length - 1) : typeName;

				if (typeSymbol is INamedTypeSymbol named && this.HasGenerateJsonDtoAttribute(named)) {
					var memberDtoName = nonNullable + "ForJson";
					props.Add((member.Name, memberDtoName + "?", PropertyKind.Plain, TypeKind.ForJson, memberDtoName, nonNullable));
					continue;
				}
				props.Add((member.Name, nonNullable + "?", PropertyKind.Plain, TypeKind.Plain, nonNullable, nonNullable));
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
				TypeKind.Plain => "e",
				_ => throw new("Unknown type kind: " + p.TypeKind)
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
				_ => throw new("Unknown property kind: " + p.TypeKind)
			};

			createModelBodyBuilder.AppendLine(setPropertyLogic);
		}


		var createModelBody = createModelBodyBuilder.ToString();

		var createJsonLinesBuilder = new StringBuilder();
		foreach (var p in props) {
			var setJsonPropertyLogic = p.PropertyKind switch {
				PropertyKind.Plain => p.TypeKind switch {
					TypeKind.Plain => $"model.{p.Name}",
					TypeKind.ForJson => $"{p.JsonItemType}.CreateJson(model.{p.Name})",
					_ => throw new("Unknown type kind:" + p.TypeKind)
				},
				PropertyKind.ReactiveProperty => p.TypeKind switch {
					TypeKind.Plain => $"model.{p.Name}.Value",
					TypeKind.ForJson => $"{p.JsonItemType}.CreateJson(model.{p.Name}.Value)",
					_ => throw new($"Unknown type kind:{p.TypeKind}")
				},
				PropertyKind.ObservableList => p.TypeKind switch {
					TypeKind.Plain => $"model.{p.Name}.ToArray()",
					TypeKind.ForJson => $"model.{p.Name}.Select(x => {p.JsonItemType}.CreateJson(x)).ToArray()",
					_ => throw new("Unknown type kind:" + p.TypeKind)
				},
				_ => throw new("Unknown property property")
			};

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