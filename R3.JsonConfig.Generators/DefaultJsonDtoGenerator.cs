using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
/// ポリモーフィズムは具象クラス側に [JsonConfigDerivedType] を付与して派生型を宣言する方式です。
/// </summary>
[Generator]
public class DefaultJsonDtoGenerator : IIncrementalGenerator {
	private static readonly SymbolDisplayFormat FullyQualifiedFormat = new SymbolDisplayFormat(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers | SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
	);

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

	/// <summary>
	/// [JsonConfigDerivedType] が付与された具象クラスの情報を保持する Equatable なモデル。
	/// Incremental Generator のキャッシュにおいて値比較で変更を検出する。
	/// netstandard2.0 では record が使用できないため、IEquatable を明示的に実装。
	/// </summary>
	private sealed class DerivedTypeEntry : IEquatable<DerivedTypeEntry> {
		/// <summary>派生型の短縮名。</summary>
		public string DerivedTypeName {
			get;
		}
		/// <summary>JSON 内で使用する型識別文字列。</summary>
		public string TypeDiscriminator {
			get;
		}
		/// <summary>[GenerateR3JsonConfigDto] が付与されている基底型の完全修飾名。</summary>
		public string BaseTypeFullName {
			get;
		}

		public DerivedTypeEntry(string derivedTypeName, string typeDiscriminator, string baseTypeFullName) {
			this.DerivedTypeName = derivedTypeName;
			this.TypeDiscriminator = typeDiscriminator;
			this.BaseTypeFullName = baseTypeFullName;
		}

		public bool Equals(DerivedTypeEntry? other) {
			if (other is null) {
				return false;
			}
			return this.DerivedTypeName == other.DerivedTypeName
				&& this.TypeDiscriminator == other.TypeDiscriminator
				&& this.BaseTypeFullName == other.BaseTypeFullName;
		}

		public override bool Equals(object? obj) {
			return this.Equals(obj as DerivedTypeEntry);
		}

		public override int GetHashCode() {
			unchecked {
				var hash = 17;
				hash = (hash * 31) + this.DerivedTypeName.GetHashCode();
				hash = (hash * 31) + this.TypeDiscriminator.GetHashCode();
				hash = (hash * 31) + this.BaseTypeFullName.GetHashCode();
				return hash;
			}
		}
	}

	public void Initialize(IncrementalGeneratorInitializationContext context) {
		// パイプライン1: [GenerateR3JsonConfigDto] 属性が付与されたクラスまたはインターフェースを抽出
		var candidates = context.SyntaxProvider
			.CreateSyntaxProvider(static (s, _) => s is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
				(ctx, _) => this.GetTarget(ctx))
			.Where(static m => m is { });

		// パイプライン2: [JsonConfigDerivedType] 属性が付与された具象クラスから派生型情報を収集
		var derivedEntries = context.SyntaxProvider
			.CreateSyntaxProvider(static (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
				(ctx, _) => this.GetDerivedTypeEntry(ctx))
			.Where(static m => m is { });

		// 両パイプラインを結合してソース生成を実行
		var combined = context.CompilationProvider
			.Combine(candidates.Collect())
			.Combine(derivedEntries.Collect());

		context.RegisterSourceOutput(combined, (spc, source) => {
			var compilation = source.Left.Left;
			var symbols = source.Left.Right;
			var entries = source.Right;
			this.Execute(spc, compilation, symbols, entries);
		});
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
	/// [JsonConfigDerivedType] が付与されたクラスから、派生型エントリを構築する。
	/// 具象クラスが実装・継承する型のうち、[GenerateR3JsonConfigDto] を持つ最初の基底型を探す。
	/// </summary>
	private DerivedTypeEntry? GetDerivedTypeEntry(GeneratorSyntaxContext ctx) {
		if (ctx.Node is not ClassDeclarationSyntax cds) {
			return null;
		}
		if (ctx.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) {
			return null;
		}

		// [JsonConfigDerivedType] 属性を探す
		string? discriminator = null;
		foreach (var attr in symbol.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() == this.DerivedTypeAttributeName) {
				if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string s) {
					discriminator = s;
					break;
				}
			}
		}
		if (discriminator is null) {
			return null;
		}

		// [GenerateR3JsonConfigDto] を持つ基底型を探す（インターフェース → 基底クラスの順で探索）
		var baseTypeFullName = this.FindGenerateJsonDtoBaseType(symbol);
		if (baseTypeFullName is null) {
			return null;
		}

		return new DerivedTypeEntry(symbol.ToDisplayString(FullyQualifiedFormat), discriminator, baseTypeFullName);
	}

	/// <summary>
	/// 指定したシンボルが実装・継承する型のうち、[GenerateR3JsonConfigDto] を持つ最初の基底型の完全修飾名を返す。
	/// 見つからない場合は null。
	/// </summary>
	private string? FindGenerateJsonDtoBaseType(INamedTypeSymbol symbol) {
		// インターフェースを先に探索
		foreach (var iface in symbol.AllInterfaces) {
			if (this.HasGenerateJsonDtoAttribute(iface)) {
				return iface.ToDisplayString(FullyQualifiedFormat);
			}
		}

		// 基底クラスを探索
		var baseType = symbol.BaseType;
		while (baseType != null && baseType.SpecialType != SpecialType.System_Object) {
			if (this.HasGenerateJsonDtoAttribute(baseType)) {
				return baseType.ToDisplayString(FullyQualifiedFormat);
			}
			baseType = baseType.BaseType;
		}

		return null;
	}

	/// <summary>
	/// 抽出されたシンボルと派生型エントリに対してソース生成処理を振り分ける。
	/// インターフェースや抽象クラスの基底型が参照先アセンブリにある場合（プロジェクト分割パターン）にも対応し、
	/// 派生型が存在するプロジェクトでポリモーフィックな ForJson クラスを生成する。
	/// </summary>
	private void Execute(SourceProductionContext context, Compilation compilation, ImmutableArray<INamedTypeSymbol?> symbols, ImmutableArray<DerivedTypeEntry?> entries) {
		var derivedMap = new Dictionary<string, List<(string TypeName, string StringKey)>>();
		foreach (var entry in entries) {
			if (entry is null) {
				continue;
			}
			if (!derivedMap.TryGetValue(entry.BaseTypeFullName, out var list)) {
				list = new List<(string TypeName, string StringKey)>();
				derivedMap[entry.BaseTypeFullName] = list;
			}
			list.Add((entry.DerivedTypeName, entry.TypeDiscriminator));
		}

		foreach (var symbol in symbols) {
			if (symbol is null) {
				continue;
			}

			try {
				this.GenerateForSymbol(context, symbol, derivedMap);
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
	private void GenerateForSymbol(SourceProductionContext context, INamedTypeSymbol modelSymbol, Dictionary<string, List<(string TypeName, string StringKey)>> derivedMap) {
		var modelName = modelSymbol.Name;
		var dtoName = modelName + "ForJson";

		var modelFullName = modelSymbol.ToDisplayString(FullyQualifiedFormat);
		var dtoFullName = modelFullName + "ForJson";

		// ケース1: ポリモーフィックな基底クラス/インターフェースの場合
		if (modelSymbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Interface || modelSymbol.IsAbstract) {
			var polymorphicCode = this.BuildPolymorphicDto(modelSymbol, modelFullName, dtoFullName, new List<(string TypeName, string StringKey)>());
			context.AddSource($"{dtoName}.g.cs", SourceText.From(polymorphicCode, Encoding.UTF8));
			return;
		}

		// ケース2: 具体的な実装クラス（または派生型を持たない基底クラス）の場合
		var inheritance = this.GetInheritance(modelSymbol);
		var props = this.GetProperties(modelSymbol);

		var concreteCode = this.BuildConcreteDto(modelSymbol, modelFullName, dtoFullName, inheritance, props);
		context.AddSource($"{dtoName}.g.cs", SourceText.From(concreteCode, Encoding.UTF8));
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
					inheritance = $" : {baseType.ToDisplayString(FullyQualifiedFormat)}ForJson";
					break;
				}
				baseType = baseType.BaseType;
			}
			if (string.IsNullOrEmpty(inheritance)) {
				foreach (var iface in modelSymbol.AllInterfaces) {
					if (this.HasGenerateJsonDtoAttribute(iface)) {
						inheritance = $" : {iface.ToDisplayString(FullyQualifiedFormat)}ForJson";
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
		var display = typeSymbol.ToDisplayString(FullyQualifiedFormat);
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
	private string BuildPolymorphicDto(INamedTypeSymbol modelSymbol, string modelFullName, string dtoFullName, List<(string TypeName, string StringKey)> derivedTypes) {
		var ns = modelSymbol.ContainingNamespace.IsGlobalNamespace ? "" : modelSymbol.ContainingNamespace.ToDisplayString();
		var namespaceLine = string.IsNullOrWhiteSpace(ns) ? string.Empty : $"namespace {ns};";

		return $$"""
// <auto-generated />
#nullable enable

{{namespaceLine}}
public partial class {{modelSymbol.Name}}ForJson {
	public string? ___Id { get; set; }

	public string? ___Ref { get; set; }

	protected virtual {{modelFullName}} CreateModelCore(global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver resolver) {
		throw new global::System.InvalidOperationException($"Unknown derived type: {this.GetType().FullName}");
	}

	[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]
	public static {{modelFullName}}? CreateModel({{dtoFullName}}? json, global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver? resolver = null) {
		if (json is null) return null;
		if (json.___Ref is { } @ref) return resolver?.Resolve<{{modelFullName}}>(@ref) ?? throw new global::System.InvalidOperationException($"Reference not found: {@ref}");
		resolver ??= new global::R3.JsonConfig.ReferenceResolver();
		return json.CreateModelCore(sp, resolver);
	}

	[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]
	public static {{dtoFullName}}? CreateJson({{modelFullName}}? model, global::R3.JsonConfig.ReferenceTracker? tracker = null) {
		if(model is null) return null;
		tracker ??= new global::R3.JsonConfig.ReferenceTracker();
		return global::R3.JsonConfig.ForJsonConverterRegistry.CreateJson<{{modelFullName}}, {{dtoFullName}}>(model, tracker);
	}
}
""";
	}

	/// <summary>
	/// 具体的な実装クラスのための DTO ソースを構築する。
	/// </summary>
	private string BuildConcreteDto(INamedTypeSymbol modelSymbol, string modelFullName, string dtoFullName, string inheritance, List<DtoPropertyInfo> props) {
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
				TypeKind.ForJson => $"{p.JsonItemType}.CreateModel(e, global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver)",
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
					TypeKind.Plain => $"global::System.Linq.Enumerable.ToArray(model.{p.Name})",
					TypeKind.ForJson => $"global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(model.{p.Name}, x => {p.JsonItemType}.CreateJson(x, tracker)))",
					_ => throw new("Unknown type kind:" + p.TypeKind)
				},
				_ => throw new("Unknown property property")
			};

			createJsonLinesBuilder.AppendLine($"\t\t\t{p.Name} = {setJsonPropertyLogic},");
		}
		var createJsonLines = createJsonLinesBuilder.ToString();

		var metadataProps = string.IsNullOrEmpty(inheritance)
			? $$"""
				public string? ___Id { get; set; }

				public string? ___Ref { get; set; }
			"""
			: "";

		var registrationCode = "";
		// DerivedTypeAttributeName returns "R3.JsonConfig.Attributes.JsonConfigDerivedTypeAttribute"
		var isDerived = modelSymbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == this.DerivedTypeAttributeName);
		if (isDerived && !string.IsNullOrEmpty(inheritance)) {
			var discriminator = "Unknown";
			foreach (var attr in modelSymbol.GetAttributes()) {
				if (attr.AttributeClass?.ToDisplayString() == this.DerivedTypeAttributeName) {
					if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string s) {
						discriminator = s;
						break;
					}
				}
			}

			// extract base model mapping to registry
			var baseTypeFullName = this.FindGenerateJsonDtoBaseType(modelSymbol);
			if (baseTypeFullName != null) {
				registrationCode = $$"""

internal static partial class {{modelSymbol.Name}}ForJsonRuntimeRegistration {
	[global::System.Runtime.CompilerServices.ModuleInitializer]
	public static void Register() {
		global::R3.JsonConfig.ForJsonConverterRegistry.Register<
			{{baseTypeFullName}},
			{{baseTypeFullName}}ForJson,
			{{modelFullName}},
			{{dtoFullName}}>("{{discriminator}}", (m, t) => {{modelSymbol.Name}}ForJson.CreateJson(m, t));
	}
}
""";
			}
		}

		var createModelCoreVisibility = string.IsNullOrEmpty(inheritance) ? "public" : "protected override";
		var resolveMethod = string.IsNullOrEmpty(inheritance) ? "CreateModel" : "CreateModelCore";
		var resolveBaseTypeFqn = string.IsNullOrEmpty(inheritance) ? modelFullName : this.FindGenerateJsonDtoBaseType(modelSymbol) ?? string.Empty;

		var startMethodModifiers = string.IsNullOrEmpty(inheritance) ?
		$$"""
	[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]
	public static {{modelFullName}}? CreateModel({{dtoFullName}}? json, global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver? resolver = null) {
		if(json is null){
			return null;
		}
		if (json.___Ref is { } @ref) return resolver?.Resolve<{{modelFullName}}>(@ref) ?? throw new global::System.InvalidOperationException($"Reference not found: {@ref}");
		resolver ??= new global::R3.JsonConfig.ReferenceResolver();

""" :
		$$"""
	{{createModelCoreVisibility}} {{resolveBaseTypeFqn}} {{resolveMethod}}(global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver resolver) {
		var json = this;

""";

		var returns = string.IsNullOrEmpty(inheritance) ? "\t\treturn model;\n\t}" : "\t\treturn model;\n\t}";


		var code = $$"""
// <auto-generated />
#nullable enable

{{namespaceLine}}
public partial class {{modelSymbol.Name}}ForJson{{inheritance}} {
{{metadataProps}}

{{propLines}}
{{startMethodModifiers}}
		var model = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{{modelFullName}}>(sp);
		if (json.___Id is { } id) resolver.Add(id, model);
{{createModelBody}}
{{returns}}

	[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]
	public static {{dtoFullName}}? CreateJson({{modelFullName}}? model, global::R3.JsonConfig.ReferenceTracker? tracker = null) {
		if (model is null){
			return null;
		}
		tracker ??= new global::R3.JsonConfig.ReferenceTracker();
		if (tracker.GetOrAddId(model) is { } id) {
			return new {{dtoFullName}} { ___Ref = id };
		}

		return new() {
			___Id = tracker.GetId(model),
{{createJsonLines}}
		};
	}
}
{{registrationCode}}
""";
		return code;
	}
}