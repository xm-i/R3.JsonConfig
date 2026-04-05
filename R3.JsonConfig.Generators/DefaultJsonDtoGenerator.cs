using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace R3.JsonConfig.Generators;

/// <summary>
/// Domain Model から JSON シリアライズ用の DTO (Data Transfer Object) と相互変換メソッドを自動生成する Incremental Generator。
/// R3 の ReactiveProperty や ObservableCollections の ObservableList に対応し、
/// System.Text.Json のポリモーフィズム (JsonPolymorphic/JsonDerivedType) もサポートします。
/// </summary>
[Generator]
public class DefaultJsonDtoGenerator : IIncrementalGenerator {
	/// <summary>生成対象を識別するための属性名。</summary>
	protected virtual string TargetAttribute {
		get;
	} = "R3.JsonConfig.Attributes.GenerateR3JsonConfigDtoAttribute";

	/// <summary>除外プロパティを識別するための属性名。</summary>
	protected virtual string ExcludePropertyAttributeName {
		get;
	} = "R3.JsonConfig.Attributes.ExcludePropertyAttribute";

	/// <summary>派生型を識別するための属性名。</summary>
	protected virtual string DerivedTypeAttributeName {
		get;
	} = "R3.JsonConfig.Attributes.JsonConfigDerivedTypeAttribute";

	/// <summary>
	/// プロパティの種類（通常のプロパティ、ReactiveProperty、ObservableList）。
	/// </summary>
	private enum PropertyKind {
		Plain,
		ReactiveProperty,
		ObservableList
	}

	/// <summary>
	/// 型の分類（通常の型、または [GenerateR3JsonConfigDto] によって DTO 変換が必要な型）。
	/// </summary>
	private enum TypeKind {
		Plain,
		ForJson
	}

	/// <summary>
	/// 生成対象となるプロパティの情報。
	/// </summary>
	private class DtoPropertyInfo {
		/// <summary>プロパティ名。</summary>
		public string Name {
			get;
		}
		/// <summary>JSON DTO で使用する型名。</summary>
		public string JsonType {
			get;
		}
		/// <summary>プロパティの種類。</summary>
		public PropertyKind PropertyKind {
			get;
		}
		/// <summary>要素またはプロパティ自体の型分類。</summary>
		public TypeKind TypeKind {
			get;
		}
		/// <summary>JSON DTO で使用する要素（または単一型）の型名。</summary>
		public string JsonItemType {
			get;
		}
		/// <summary>元の型の Nullable ではない完全修飾名。</summary>
		public string NonNullableItemTypeFullName {
			get;
		}

		public DtoPropertyInfo(string name, string jsonType, PropertyKind propertyKind, TypeKind typeKind, string jsonItemType, string nonNullableItemTypeFullName) {
			this.Name = name;
			this.JsonType = jsonType;
			this.PropertyKind = propertyKind;
			this.TypeKind = typeKind;
			this.JsonItemType = jsonItemType;
			this.NonNullableItemTypeFullName = nonNullableItemTypeFullName;
		}
	}

	public void Initialize(IncrementalGeneratorInitializationContext context) {
		// [GenerateR3JsonConfigDto] 属性が付与されたクラスまたはインターフェースを抽出
		var candidates = context.SyntaxProvider
			.CreateSyntaxProvider(static (s, _) => s is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
				(ctx, _) => this.GetTarget(ctx))
			.Where(static m => m is { });

		// コンパイル情報とモデル情報を結合して、ソース生成を実行
		var compilationAndModels = context.CompilationProvider.Combine(candidates.Collect());
		context.RegisterSourceOutput(compilationAndModels, (spc, source) => this.Execute(spc, source.Left, source.Right));
	}

	/// <summary>
	/// 構文ノードから対象となるシンボルを取得。属性の有無を確認。
	/// </summary>
	private INamedTypeSymbol? GetTarget(GeneratorSyntaxContext ctx) {
		if (ctx.Node is not TypeDeclarationSyntax cds || cds is RecordDeclarationSyntax) {
			return null;
		}
		if (ctx.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) {
			return null;
		}

		// [GenerateR3JsonConfigDto] の AttributeTargets は Class | Interface
		if (symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Class && symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Interface) {
			return null;
		}

		if (!symbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == this.TargetAttribute)) {
			return null;
		}

		if (!this.HasGenerateJsonDtoAttribute(symbol)) {
			return null;
		}

		return symbol;
	}

	/// <summary>
	/// 抽出されたシンボルに対してソース生成処理を振り分ける。
	/// </summary>
	private void Execute(SourceProductionContext context, Compilation _, IEnumerable<INamedTypeSymbol> symbols) {
		foreach (var symbol in symbols) {
			try {
				this.GenerateForSymbol(context, symbol);
			} catch (Exception ex) {
				context.ReportDiagnostic(Diagnostic.Create(new("RJG001", "JsonDtoGenerator Error", "{0}", "JsonDtoGenerator", DiagnosticSeverity.Warning, true), Location.None, ex.Message));
			}
		}
	}

	/// <summary>
	/// 指定したシンボルが [GenerateR3JsonConfigDto] 属性を持っているか確認する。
	/// </summary>
	private bool HasGenerateJsonDtoAttribute(INamedTypeSymbol symbol) {
		foreach (var attr in symbol.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() == this.TargetAttribute) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 特定のシンボルに対してソースを生成。
	/// </summary>
	private void GenerateForSymbol(SourceProductionContext context, INamedTypeSymbol modelSymbol) {
		var modelName = modelSymbol.Name;
		var dtoName = modelName + "ForJson";

		// ポリモーフィック対応の解析
		var derivedTypes = this.GetDerivedTypes(modelSymbol);

		// ケース1: ポリモーフィックな基底クラス/インターフェースの場合
		if (derivedTypes.Count > 0) {
			var polymorphicCode = this.BuildPolymorphicDto(modelSymbol, modelName, dtoName, derivedTypes);
			context.AddSource($"{dtoName}.g.cs", SourceText.From(polymorphicCode, Encoding.UTF8));
			return;
		}

		// ケース2: 具体的な実装クラス（または派生型を持たない基底クラス）の場合
		var inheritance = this.GetInheritance(modelSymbol);
		var props = this.GetProperties(modelSymbol);

		var concreteCode = this.BuildConcreteDto(modelSymbol, modelName, dtoName, inheritance, props);
		context.AddSource($"{dtoName}.g.cs", SourceText.From(concreteCode, Encoding.UTF8));
	}

	/// <summary>
	/// [JsonConfigDerivedType] から派生型の情報を取得する。
	/// </summary>
	private List<(string TypeName, string StringKey)> GetDerivedTypes(INamedTypeSymbol modelSymbol) {
		var derivedTypes = new List<(string TypeName, string StringKey)>();
		foreach (var attr in modelSymbol.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() == this.DerivedTypeAttributeName) {
				if (attr.ConstructorArguments.Length == 2) {
					if (attr.ConstructorArguments[0].Value is ITypeSymbol t && attr.ConstructorArguments[1].Value is string s) {
						derivedTypes.Add((t.Name, s));
					}
				}
			}
		}
		return derivedTypes;
	}

	/// <summary>
	/// 継承関係を解析し、基底クラスやインターフェースの DTO が存在するか確認する。
	/// </summary>
	private string GetInheritance(INamedTypeSymbol modelSymbol) {
		var inheritance = "";
		if (modelSymbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Class) {
			var baseType = modelSymbol.BaseType;
			while (baseType != null && baseType.SpecialType != SpecialType.System_Object) {
				if (this.HasGenerateJsonDtoAttribute(baseType)) {
					inheritance = $" : {baseType.Name}ForJson";
					break;
				}
				baseType = baseType.BaseType;
			}
			if (string.IsNullOrEmpty(inheritance)) {
				foreach (var iface in modelSymbol.AllInterfaces) {
					if (this.HasGenerateJsonDtoAttribute(iface)) {
						inheritance = $" : {iface.Name}ForJson";
						break;
					}
				}
			}
		}
		return inheritance;
	}

	/// <summary>
	/// 型を解析し、DTO への変換が必要かどうか、および生成に使用する型名を決定する。
	/// </summary>
	/// <param name="typeSymbol">解析対象の型シンボル。</param>
	/// <returns>解析結果（種類、DTO型名、非Nullable型フルネーム）。</returns>
	private (TypeKind TypeKind, string JsonItemType, string NonNullableItemTypeFullName) ResolveType(ITypeSymbol typeSymbol) {
		var display = typeSymbol.ToDisplayString();
		var nonNullable = display.TrimEnd('?');

		// [GenerateR3JsonConfigDto] が付与されている場合は DTO 変換対象
		if (typeSymbol is INamedTypeSymbol named && this.HasGenerateJsonDtoAttribute(named)) {
			var dtoName = nonNullable + "ForJson";
			return (TypeKind.ForJson, dtoName, nonNullable);
		}

		// それ以外は通常の型として扱う
		return (TypeKind.Plain, display, nonNullable);
	}

	/// <summary>
	/// 型名が Nullable ではない場合に '?' を付与する。
	/// </summary>
	/// <param name="typeName">対象の型名。</param>
	/// <returns>Nullable になった型名。</returns>
	private string MakeNullable(string typeName) {
		return typeName.EndsWith("?") ? typeName : typeName + "?";
	}

	/// <summary>
	/// モデルのプロパティを解析し、生成に必要な情報を収集する。
	/// </summary>
	/// <param name="modelSymbol">解析対象のモデルのシンボル。</param>
	/// <returns>収集されたプロパティ情報のリスト。</returns>
	private List<DtoPropertyInfo> GetProperties(INamedTypeSymbol modelSymbol) {
		var props = new List<DtoPropertyInfo>();
		foreach (var member in modelSymbol.GetMembers().OfType<IPropertySymbol>()) {
			if (member.DeclaredAccessibility != Accessibility.Public) {
				continue;
			}

			// Skip properties with ExcludePropertyAttribute
			if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == this.ExcludePropertyAttributeName)) {
				continue;
			}

			var typeSymbol = member.Type;

			// コレクション・ラップ型の特殊処理
			switch (typeSymbol) {
				case INamedTypeSymbol { TypeArguments.Length: 1, MetadataName: "ObservableList`1" } nts when nts.ContainingNamespace.ToDisplayString() == "ObservableCollections": {
						var resolved = this.ResolveType(nts.TypeArguments[0]);
						// 要素の型が既に Nullable かどうかに関わらず、配列自体は Nullable にする
						props.Add(new(member.Name, $"{resolved.JsonItemType}[]?", PropertyKind.ObservableList, resolved.TypeKind, resolved.JsonItemType, resolved.NonNullableItemTypeFullName));
						continue;
					}
				case INamedTypeSymbol { TypeArguments.Length: 1, MetadataName: "ReactiveProperty`1" } reactive: {
						var resolved = this.ResolveType(reactive.TypeArguments[0]);
						props.Add(new(member.Name, this.MakeNullable(resolved.JsonItemType), PropertyKind.ReactiveProperty, resolved.TypeKind, resolved.JsonItemType, resolved.NonNullableItemTypeFullName));
						continue;
					}
			}

			// 通常のプロパティ（setter が public なもののみ）
			var settableProperty = member.SetMethod is { DeclaredAccessibility: Accessibility.Public };
			if (!settableProperty) {
				continue;
			}
			{
				var resolved = this.ResolveType(typeSymbol);
				props.Add(new(member.Name, this.MakeNullable(resolved.JsonItemType), PropertyKind.Plain, resolved.TypeKind, resolved.JsonItemType, resolved.NonNullableItemTypeFullName));
			}
		}
		return props;
	}

	/// <summary>
	/// ポリモーフィックなインターフェースまたは基底クラスのための DTO ソースを構築する。
	/// </summary>
	private string BuildPolymorphicDto(INamedTypeSymbol modelSymbol, string modelName, string dtoName, List<(string TypeName, string StringKey)> derivedTypes) {
		var ns = modelSymbol.ContainingNamespace.IsGlobalNamespace ? "" : modelSymbol.ContainingNamespace.ToDisplayString();
		var namespaceLine = string.IsNullOrWhiteSpace(ns) ? string.Empty : $"namespace {ns};";

		var createModelBodyBuilderP = new StringBuilder();
		var createJsonLinesBuilderP = new StringBuilder();

		foreach (var dt in derivedTypes) {
			var derivedDto = dt.TypeName + "ForJson";
			var varNameM = "e_" + dt.TypeName;
			createModelBodyBuilderP.AppendLine($$"""
		if (json is {{derivedDto}} {{varNameM}}) {
			return {{derivedDto}}.CreateModel({{varNameM}}, sp.CreateScope().ServiceProvider, resolver);
		}
""");

			var varNameJ = "m_" + dt.TypeName;
			createJsonLinesBuilderP.AppendLine($$"""
		if (model is {{dt.TypeName}} {{varNameJ}}) {
			return {{derivedDto}}.CreateJson({{varNameJ}}, tracker);
		}
""");
		}
		createModelBodyBuilderP.AppendLine("\t\tthrow new System.InvalidOperationException($\"Unknown derived type: {json?.GetType().FullName}\");");
		createJsonLinesBuilderP.AppendLine("\t\tthrow new System.InvalidOperationException($\"Unknown derived type: {model?.GetType().FullName}\");");

		var attrsBuilder = new StringBuilder();
		attrsBuilder.AppendLine("\t[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = \"___Type\")]");
		foreach (var dt in derivedTypes) {
			attrsBuilder.AppendLine($"\t[System.Text.Json.Serialization.JsonDerivedType(typeof({dt.TypeName}ForJson), \"{dt.StringKey}\")]");
		}

		return $$"""
// <auto-generated />
#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Text.Json.Serialization;
using R3.JsonConfig;

{{namespaceLine}}
{{attrsBuilder.ToString().TrimEnd()}}
public partial class {{dtoName}} {
	[JsonPropertyName("__id")]
	public string? Id { get; set; }

	[JsonPropertyName("__ref")]
	public string? Ref { get; set; }

	[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]
	public static {{modelName}}? CreateModel({{dtoName}}? json, System.IServiceProvider sp, ReferenceResolver? resolver = null) {
		if (json is null) return null;
		if (json.Ref is { } @ref) return resolver?.Resolve<{{modelName}}>(@ref) ?? throw new System.InvalidOperationException($"Reference not found: {@ref}");
		resolver ??= new ReferenceResolver();
{{createModelBodyBuilderP.ToString().TrimEnd()}}
	}

	[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]
	public static {{dtoName}}? CreateJson({{modelName}}? model, ReferenceTracker? tracker = null) {
		if(model is null) return null;
		tracker ??= new ReferenceTracker();
{{createJsonLinesBuilderP.ToString().TrimEnd()}}
	}
}
""";
	}

	/// <summary>
	/// 具体的な実装クラスのための DTO ソースを構築する。
	/// </summary>
	private string BuildConcreteDto(INamedTypeSymbol modelSymbol, string modelName, string dtoName, string inheritance, List<DtoPropertyInfo> props) {
		var ns = modelSymbol.ContainingNamespace.IsGlobalNamespace ? "" : modelSymbol.ContainingNamespace.ToDisplayString();
		var namespaceLine = string.IsNullOrWhiteSpace(ns) ? string.Empty : $"namespace {ns};";

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
				TypeKind.ForJson => $"{p.JsonItemType}.CreateModel(e, sp.CreateScope().ServiceProvider, resolver)",
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
					TypeKind.ForJson => $"{p.JsonItemType}.CreateJson(model.{p.Name}, tracker)",
					_ => throw new("Unknown type kind:" + p.TypeKind)
				},
				PropertyKind.ReactiveProperty => p.TypeKind switch {
					TypeKind.Plain => $"model.{p.Name}.Value",
					TypeKind.ForJson => $"{p.JsonItemType}.CreateJson(model.{p.Name}.Value, tracker)",
					_ => throw new($"Unknown type kind:{p.TypeKind}")
				},
				PropertyKind.ObservableList => p.TypeKind switch {
					TypeKind.Plain => $"model.{p.Name}.ToArray()",
					TypeKind.ForJson => $"model.{p.Name}.Select(x => {p.JsonItemType}.CreateJson(x, tracker)).ToArray()",
					_ => throw new("Unknown type kind:" + p.TypeKind)
				},
				_ => throw new("Unknown property property")
			};

			createJsonLinesBuilder.AppendLine($"\t\t\t{p.Name} = {setJsonPropertyLogic},");
		}
		var createJsonLines = createJsonLinesBuilder.ToString();

		var metadataProps = string.IsNullOrEmpty(inheritance)
			? $$"""
				[JsonPropertyName("__id")]
				public string? Id { get; set; }

				[JsonPropertyName("__ref")]
				public string? Ref { get; set; }
			"""
			: "";

		return $$"""
			// <auto-generated />
			#nullable enable
			using Microsoft.Extensions.DependencyInjection;
			using System.Linq;
			using System.Text.Json.Serialization;
			using R3.JsonConfig;

			{{namespaceLine}}
			public partial class {{dtoName}}{{inheritance}} {
			{{metadataProps}}

			{{propLines}}
				[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]
				public static {{modelName}}? CreateModel({{dtoName}}? json, System.IServiceProvider sp, ReferenceResolver? resolver = null) {
					if(json is null){
						return null;
					}
					if (json.Ref is { } @ref) return resolver?.Resolve<{{modelName}}>(@ref) ?? throw new System.InvalidOperationException($"Reference not found: {@ref}");
					resolver ??= new ReferenceResolver();

					var model = sp.GetRequiredService<{{modelName}}>();
					if (json.Id is { } id) resolver.Add(id, model);
			{{createModelBody}}
					return model;
				}

				[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]
				public static {{dtoName}}? CreateJson({{modelName}}? model, ReferenceTracker? tracker = null) {
					if (model is null){
						return null;
					}
					tracker ??= new ReferenceTracker();
					if (tracker.GetOrAddId(model) is { } id) {
						return new {{dtoName}} { Ref = id };
					}

					return new() {
						Id = tracker.GetId(model),
			{{createJsonLines}}
					};
				}
			}
			""";
	}
}