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
/// Domain Model から JSON シリアライズ用の DTO (Data Transfer Object) と
/// 相互変換メソッド（<c>CreateModel</c> / <c>CreateJson</c>）を自動生成する Incremental Generator。
/// <para>
/// ラッパー型・コレクション型の判定は戦略パターン（<see cref="IPropertyStrategy"/> チェーン）に委譲しており、
/// <c>[assembly: RegisterJsonConfigWrapper]</c> 属性による任意のラッパー型の登録が可能。
/// </para>
/// <para>
/// System.Text.Json のポリモーフィズム（JsonPolymorphic / JsonDerivedType）もサポートします。
/// </para>
/// </summary>
[Generator]
public class DefaultJsonDtoGenerator : IIncrementalGenerator {

	// --------------------------------------------------------
	// 定数・フォーマット定義
	// --------------------------------------------------------

	/// <summary>
	/// 型シンボルを完全修飾名（<c>global::</c> プレフィックス付き、nullable 修飾子付き）で
	/// 文字列化するフォーマット。生成コードに埋め込む型名に使用する。
	/// </summary>
	private static readonly SymbolDisplayFormat FullyQualifiedFormat = new SymbolDisplayFormat(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
		miscellaneousOptions:
			SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
			SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
			SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
	);

	// --------------------------------------------------------
	// 属性名定数（protected virtual にして派生クラスで差し替え可能）
	// --------------------------------------------------------

	/// <summary>DTO 生成のトリガーとなる属性の完全修飾名。</summary>
	protected virtual string TargetAttribute {
		get;
	}
		= "R3.JsonConfig.Attributes.GenerateR3JsonConfigDtoAttribute";

	/// <summary>プロパティをDTO生成対象から除外する属性の完全修飾名。</summary>
	protected virtual string ExcludePropertyAttributeName {
		get;
	}
		= "R3.JsonConfig.Attributes.ExcludePropertyAttribute";

	/// <summary>派生型を登録する属性の完全修飾名。</summary>
	protected virtual string DerivedTypeAttributeName {
		get;
	}
		= "R3.JsonConfig.Attributes.JsonConfigDerivedTypeAttribute";

	/// <summary>プロパティが DI スコープを生成することを示す属性の完全修飾名。</summary>
	protected virtual string CreateScopePropertyAttributeName {
		get;
	}
		= "R3.JsonConfig.Attributes.JsonConfigCreateScopeAttribute";

	// --------------------------------------------------------
	// プロパティ解決戦略チェーン
	// --------------------------------------------------------

	/// <summary>
	/// プロパティアクセス戦略のチェーン。
	/// <see cref="GetProperties"/> 内で先頭から順番に試行し、
	/// 最初に成功した戦略の <see cref="AccessStrategy"/> を採用する。
	/// 派生クラスでオーバーライドすることで戦略を追加・差し替えできる。
	/// </summary>
	internal virtual IReadOnlyList<IPropertyStrategy> PropertyStrategies {
		get;
	}
		= new IPropertyStrategy[] {
			new WrapperPropertyStrategy(),
			new CollectionPropertyStrategy(),
			new PlainPropertyStrategy(),
		};

	// --------------------------------------------------------
	// 内部データクラス
	// --------------------------------------------------------

	/// <summary>
	/// ポリモーフィズム用の派生型エントリ。
	/// <c>[JsonConfigDerivedType("discriminator")]</c> が付いたクラスごとに作成し、
	/// 基底型の FQN をキーとした辞書に格納する。
	/// </summary>
	private sealed class DerivedTypeEntry : IEquatable<DerivedTypeEntry> {
		/// <summary>派生型の完全修飾名（<c>global::</c> プレフィックス付き）。</summary>
		public string DerivedTypeName {
			get;
		}

		/// <summary>JSON ポリモーフィズムで使用する型識別子文字列。</summary>
		public string TypeDiscriminator {
			get;
		}

		/// <summary>対応する基底型の完全修飾名（辞書のキーとして使用）。</summary>
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
				var h = 17;
				h = (h * 31) + this.DerivedTypeName.GetHashCode();
				h = (h * 31) + this.TypeDiscriminator.GetHashCode();
				h = (h * 31) + this.BaseTypeFullName.GetHashCode();
				return h;
			}
		}
	}

	/// <summary>
	/// プロパティ 1 件の生成情報を保持する内部データクラス。
	/// <see cref="GetProperties"/> で作成し、<see cref="BuildConcreteDto"/> に渡す。
	/// </summary>
	private sealed class DtoPropertyInfo {
		/// <summary>プロパティ名（C# シンボル名と同一）。</summary>
		public string Name {
			get;
		}

		/// <summary>このプロパティに対して解決されたアクセス戦略。</summary>
		public AccessStrategy Strategy {
			get;
		}

		/// <summary>
		/// このプロパティを <c>CreateModel</c> 内で復元するとき、
		/// 子オブジェクトを独立した DI スコープで生成するかどうか。
		/// </summary>
		public bool CreateScope {
			get;
		}

		public DtoPropertyInfo(string name, AccessStrategy strategy, bool createScope) {
			this.Name = name;
			this.Strategy = strategy;
			this.CreateScope = createScope;
		}
	}

	// --------------------------------------------------------
	// IIncrementalGenerator 実装
	// --------------------------------------------------------

	/// <summary>
	/// インクリメンタルジェネレータのエントリポイント。
	/// SyntaxProvider で対象型と派生型エントリを収集し、
	/// CompilationProvider と結合して <see cref="Execute"/> に渡す。
	/// </summary>
	public void Initialize(IncrementalGeneratorInitializationContext context) {
		// DTO 生成対象型の収集（TargetAttribute が付いた非 record クラス／インターフェース）
		var candidates = context.SyntaxProvider
			.CreateSyntaxProvider(
				static (s, _) => s is TypeDeclarationSyntax { AttributeLists.Count: > 0 },
				(ctx, _) => this.GetTarget(ctx))
			.Where(static m => m is { });

		// ポリモーフィズム用派生型エントリの収集（DerivedTypeAttributeName が付いたクラス）
		var derivedEntries = context.SyntaxProvider
			.CreateSyntaxProvider(
				static (s, _) => s is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
				(ctx, _) => this.GetDerivedTypeEntry(ctx))
			.Where(static m => m is { });

		// Compilation・候補型・派生型エントリを結合して Execute へ
		var combined = context.CompilationProvider
			.Combine(candidates.Collect())
			.Combine(derivedEntries.Collect());

		context.RegisterSourceOutput(combined, (spc, source) =>
			this.Execute(spc, source.Left.Left, source.Left.Right, source.Right));
	}

	// --------------------------------------------------------
	// 収集フェーズ
	// --------------------------------------------------------

	/// <summary>
	/// 構文ノードが DTO 生成対象かどうかを判定し、対象なら型シンボルを返す。
	/// record・クラス／インターフェース以外・<see cref="TargetAttribute"/> なし の場合は <c>null</c>。
	/// </summary>
	private INamedTypeSymbol? GetTarget(GeneratorSyntaxContext ctx) {
		// record は対象外
		if (ctx.Node is not TypeDeclarationSyntax cds || cds is RecordDeclarationSyntax) {
			return null;
		}

		if (ctx.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) {
			return null;
		}

		// クラスまたはインターフェースのみ対象
		if (symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Class
		 && symbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Interface) {
			return null;
		}

		if (!this.HasGenerateJsonDtoAttribute(symbol)) {
			return null;
		}

		return symbol;
	}

	/// <summary>
	/// 構文ノードが派生型エントリを持つかどうかを判定し、持つなら <see cref="DerivedTypeEntry"/> を返す。
	/// <c>[JsonConfigDerivedType("discriminator")]</c> が付いており、かつ DTO 生成対象の基底型を持つ場合のみ返す。
	/// </summary>
	private DerivedTypeEntry? GetDerivedTypeEntry(GeneratorSyntaxContext ctx) {
		if (ctx.Node is not ClassDeclarationSyntax cds) {
			return null;
		}

		if (ctx.SemanticModel.GetDeclaredSymbol(cds) is not INamedTypeSymbol symbol) {
			return null;
		}

		// DerivedTypeAttributeName から識別子文字列を取得
		string? discriminator = null;
		foreach (var attr in symbol.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() == this.DerivedTypeAttributeName
			 && attr.ConstructorArguments.Length == 1
			 && attr.ConstructorArguments[0].Value is string s) {
				discriminator = s;
				break;
			}
		}
		if (discriminator is null) {
			return null;
		}

		// DTO 生成対象の基底型を検索
		var baseTypeFullName = this.FindGenerateJsonDtoBaseType(symbol);
		if (baseTypeFullName is null) {
			return null;
		}

		return new DerivedTypeEntry(
			symbol.ToDisplayString(FullyQualifiedFormat),
			discriminator,
			baseTypeFullName);
	}

	/// <summary>
	/// シンボルの継承ツリー（インターフェースを含む）の中から
	/// DTO 生成対象（<see cref="TargetAttribute"/> 付き）の基底型を探し、
	/// 見つかれば完全修飾名を返す。
	/// </summary>
	private string? FindGenerateJsonDtoBaseType(INamedTypeSymbol symbol) {
		// 実装インターフェースを先に検索
		foreach (var iface in symbol.AllInterfaces) {
			if (this.HasGenerateJsonDtoAttribute(iface)) {
				return iface.ToDisplayString(FullyQualifiedFormat);
			}
		}

		// 基底クラスチェーンを検索（object は除外）
		var baseType = symbol.BaseType;
		while (baseType != null && baseType.SpecialType != SpecialType.System_Object) {
			if (this.HasGenerateJsonDtoAttribute(baseType)) {
				return baseType.ToDisplayString(FullyQualifiedFormat);
			}

			baseType = baseType.BaseType;
		}

		return null;
	}

	// --------------------------------------------------------
	// 生成フェーズ（Execute）
	// --------------------------------------------------------

	/// <summary>
	/// 収集した型シンボルと派生型エントリを元に、各型のソースファイルを生成する。
	/// </summary>
	private void Execute(
		SourceProductionContext context,
		Compilation compilation,
		ImmutableArray<INamedTypeSymbol?> symbols,
		ImmutableArray<DerivedTypeEntry?> entries) {

		// 派生型エントリを「基底型 FQN → 派生型リスト」の辞書に変換
		var derivedMap = new Dictionary<string, List<(string TypeName, string StringKey)>>();
		foreach (var entry in entries) {
			if (entry is null) {
				continue;
			}

			if (!derivedMap.TryGetValue(entry.BaseTypeFullName, out var list)) {
				list = new List<(string, string)>();
				derivedMap[entry.BaseTypeFullName] = list;
			}
			list.Add((entry.DerivedTypeName, entry.TypeDiscriminator));
		}

		// WrapperRegistry をコンパイル情報から構築し、戦略解決コンテキストを作成
		var registry = WrapperRegistry.Build(compilation);
		var resolverCtx = new ResolverContext(registry, this.HasGenerateJsonDtoAttribute, FullyQualifiedFormat);

		// 各対象型についてソース生成
		foreach (var symbol in symbols) {
			if (symbol is null) {
				continue;
			}

			try {
				this.GenerateForSymbol(context, symbol, derivedMap, resolverCtx);
			} catch (Exception ex) {
				// 予期しない例外は警告 Diagnostic として報告し、他の型の生成を続行する
				context.ReportDiagnostic(Diagnostic.Create(
					new("RJG001", "JsonDtoGenerator Error", "{0}", "JsonDtoGenerator",
						DiagnosticSeverity.Warning, isEnabledByDefault: true),
					Location.None, ex.Message));
			}
		}
	}

	/// <summary>
	/// 型シンボルに <see cref="TargetAttribute"/> が付いているかどうかを返す。
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
	/// 1 つの型シンボルに対してソースを生成し、<paramref name="context"/> に追加する。
	/// インターフェースまたは抽象クラスはポリモーフィック DTO、
	/// 具象クラスは通常の DTO として生成する。
	/// </summary>
	private void GenerateForSymbol(
		SourceProductionContext context,
		INamedTypeSymbol modelSymbol,
		Dictionary<string, List<(string TypeName, string StringKey)>> derivedMap,
		ResolverContext resolverCtx) {

		var modelName = modelSymbol.Name;
		var dtoName = modelName + "ForJson";
		var modelFullName = modelSymbol.ToDisplayString(FullyQualifiedFormat);
		var dtoFullName = modelFullName + "ForJson";

		if (modelSymbol.TypeKind == Microsoft.CodeAnalysis.TypeKind.Interface
		 || modelSymbol.IsAbstract) {
			// ポリモーフィック DTO（インターフェース・抽象クラス）
			var derived = derivedMap.TryGetValue(modelFullName, out var list)
				? list
				: new List<(string, string)>();
			context.AddSource(
				$"{dtoName}.g.cs",
				SourceText.From(this.BuildPolymorphicDto(modelSymbol, modelFullName, dtoFullName, derived), Encoding.UTF8));
			return;
		}

		// 具象クラス用 DTO
		var inheritance = this.GetInheritance(modelSymbol);
		var props = this.GetProperties(modelSymbol, resolverCtx);
		context.AddSource(
			$"{dtoName}.g.cs",
			SourceText.From(this.BuildConcreteDto(modelSymbol, modelFullName, dtoFullName, inheritance, props), Encoding.UTF8));
	}

	// --------------------------------------------------------
	// プロパティ解析ヘルパー
	// --------------------------------------------------------

	/// <summary>
	/// 継承元に DTO 生成対象の型が存在する場合、継承宣言文字列（<c>" : BaseForJson"</c> 等）を返す。
	/// 存在しない場合は空文字列を返す。
	/// </summary>
	private string GetInheritance(INamedTypeSymbol modelSymbol) {
		if (modelSymbol.TypeKind != Microsoft.CodeAnalysis.TypeKind.Class) {
			return "";
		}

		// 基底クラスチェーンを検索（object は除外）
		var baseType = modelSymbol.BaseType;
		while (baseType != null && baseType.SpecialType != SpecialType.System_Object) {
			if (this.HasGenerateJsonDtoAttribute(baseType)) {
				return $" : {baseType.ToDisplayString(FullyQualifiedFormat)}ForJson";
			}

			baseType = baseType.BaseType;
		}

		// 実装インターフェースを検索
		foreach (var iface in modelSymbol.AllInterfaces) {
			if (this.HasGenerateJsonDtoAttribute(iface)) {
				return $" : {iface.ToDisplayString(FullyQualifiedFormat)}ForJson";
			}
		}

		return "";
	}

	/// <summary>
	/// モデル型の公開プロパティを列挙し、各プロパティに対して戦略チェーンを適用して
	/// <see cref="DtoPropertyInfo"/> のリストを返す。
	/// <c>[ExcludeProperty]</c> が付いたプロパティはスキップする。
	/// </summary>
	private List<DtoPropertyInfo> GetProperties(INamedTypeSymbol modelSymbol, ResolverContext resolverCtx) {
		var props = new List<DtoPropertyInfo>();

		foreach (var member in modelSymbol.GetMembers().OfType<IPropertySymbol>()) {
			// 非公開プロパティはスキップ
			if (member.DeclaredAccessibility != Accessibility.Public) {
				continue;
			}

			// [ExcludeProperty] が付いたプロパティはスキップ
			if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == this.ExcludePropertyAttributeName)) {
				continue;
			}

			// [JsonConfigCreateScope] が付いているかどうかを記録
			var createScope = member.GetAttributes()
				.Any(a => a.AttributeClass?.ToDisplayString() == this.CreateScopePropertyAttributeName);

			// 戦略チェーンで最初に成功した戦略を採用
			AccessStrategy? strategy = null;
			foreach (var s in this.PropertyStrategies) {
				if (s.TryResolve(member, resolverCtx, out strategy) && strategy is not null) {
					break;
				}
			}
			if (strategy is null) {
				continue;
			}

			props.Add(new DtoPropertyInfo(member.Name, strategy, createScope));
		}

		return props;
	}

	// --------------------------------------------------------
	// コード生成：ポリモーフィック DTO
	// --------------------------------------------------------

	/// <summary>
	/// インターフェース・抽象クラス用のポリモーフィック DTO クラスを生成する。
	/// <c>CreateModelCore</c> は派生 DTO でオーバーライドされ、
	/// <c>CreateModel</c> は <c>$ref</c> の解決後に <c>CreateModelCore</c> を呼び出す。
	/// </summary>
	private string BuildPolymorphicDto(
		INamedTypeSymbol modelSymbol,
		string modelFullName,
		string dtoFullName,
		List<(string TypeName, string StringKey)> derivedTypes) {

		var ns = modelSymbol.ContainingNamespace.IsGlobalNamespace
			? ""
			: modelSymbol.ContainingNamespace.ToDisplayString();
		var namespaceLine = string.IsNullOrWhiteSpace(ns) ? string.Empty : $"namespace {ns};";

		return
$@"// <auto-generated />
#nullable enable

{namespaceLine}
public partial class {modelSymbol.Name}ForJson {{
	/// <summary>JSON 参照 ($id) 用 ID。</summary>
	public string? ___Id {{ get; set; }}

	/// <summary>JSON 参照 ($ref) 用参照文字列。</summary>
	public string? ___Ref {{ get; set; }}

	/// <summary>
	/// 派生型 DTO でオーバーライドし、モデルオブジェクトを復元する。
	/// 基底では常に例外をスローする。
	/// </summary>
	protected virtual {modelFullName} CreateModelCore(global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver resolver) {{
		throw new global::System.InvalidOperationException($""Unknown derived type: {{this.GetType().FullName}}"");
	}}

	/// <summary>
	/// JSON DTO からモデルオブジェクトを復元する。
	/// <c>$ref</c> が存在する場合は <paramref name=""resolver""/> で解決する。
	/// </summary>
	[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]
	public static {modelFullName}? CreateModel({dtoFullName}? json, global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver? resolver = null) {{
		if (json is null) return null;
		if (json.___Ref is {{ }} @ref)
			return resolver?.Resolve<{modelFullName}>(@ref)
				?? throw new global::System.InvalidOperationException($""Reference not found: {{@ref}}"");
		resolver ??= new global::R3.JsonConfig.ReferenceResolver();
		return json.CreateModelCore(sp, resolver);
	}}

	/// <summary>
	/// モデルオブジェクトから JSON DTO を生成する。
	/// 循環参照は <paramref name=""tracker""/> で検出し、<c>$ref</c> として出力する。
	/// </summary>
	[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]
	public static {dtoFullName}? CreateJson({modelFullName}? model, global::R3.JsonConfig.ReferenceTracker? tracker = null) {{
		if (model is null) return null;
		tracker ??= new global::R3.JsonConfig.ReferenceTracker();
		return global::R3.JsonConfig.ForJsonConverterRegistry.CreateJson<{modelFullName}, {dtoFullName}>(model, tracker);
	}}
}}";
	}

	// --------------------------------------------------------
	// コード生成：具象クラス DTO
	// --------------------------------------------------------

	/// <summary>
	/// 具象クラス用の DTO クラスを生成する。
	/// <list type="bullet">
	///   <item>DTO プロパティ宣言</item>
	///   <item><c>CreateModel</c> または <c>CreateModelCore</c>（継承時）</item>
	///   <item><c>CreateJson</c></item>
	///   <item>派生型の場合は ModuleInitializer による <c>ForJsonConverterRegistry</c> への登録コード</item>
	/// </list>
	/// </summary>
	private string BuildConcreteDto(
		INamedTypeSymbol modelSymbol,
		string modelFullName,
		string dtoFullName,
		string inheritance,
		List<DtoPropertyInfo> props) {

		var ns = modelSymbol.ContainingNamespace.IsGlobalNamespace
			? ""
			: modelSymbol.ContainingNamespace.ToDisplayString();
		var namespaceLine = string.IsNullOrWhiteSpace(ns) ? string.Empty : $"namespace {ns};";

		// --- DTO プロパティ宣言 ---
		var propLinesBuilder = new StringBuilder();
		foreach (var p in props) {
			propLinesBuilder.AppendLine($"\t\tpublic {p.Strategy.JsonPropertyType} {p.Name} {{");
			propLinesBuilder.AppendLine("\t\t\tget;");
			propLinesBuilder.AppendLine("\t\t\tset;");
			propLinesBuilder.AppendLine("\t\t}");
			propLinesBuilder.AppendLine();
		}

		// --- CreateModel 本体（プロパティをモデルに反映する部分） ---
		var createModelBodyBuilder = new StringBuilder();
		foreach (var p in props) {
			var varName = "notNull" + p.Name;
			var strategy = p.Strategy;

			if (strategy.Kind == AccessKind.Collection) {
				// コレクション型: null チェック後に Clear して foreach で Add
				var elemConvert = BuildElementToModelExpr(strategy.ElementType, p.CreateScope, "e");
				createModelBodyBuilder.AppendLine($"\t\t\tif (json.{p.Name} is {{ }} {varName}) {{");
				createModelBodyBuilder.AppendLine($"\t\t\t\tmodel.{p.Name}.Clear();");
				createModelBodyBuilder.AppendLine($"\t\t\t\tforeach (var e in {varName}) {{");
				createModelBodyBuilder.AppendLine($"\t\t\t\t\tmodel.{p.Name}.Add({elemConvert});");
				createModelBodyBuilder.AppendLine("\t\t\t\t}");
				createModelBodyBuilder.AppendLine("\t\t\t}");
			} else {
				// 単一値型（Plain / Wrapped）: null チェック後に戦略の SetterStmt で代入
				var elemConvert = BuildElementToModelExpr(strategy.ElementType, p.CreateScope, "e");
				var setterStmt = strategy.SetterStmt("model." + p.Name, elemConvert);
				createModelBodyBuilder.AppendLine($"\t\t\tif (json.{p.Name} is {{ }} {varName}) {{");
				createModelBodyBuilder.AppendLine($"\t\t\t\tvar e = {varName};");
				createModelBodyBuilder.AppendLine($"\t\t\t\t{setterStmt}");
				createModelBodyBuilder.AppendLine("\t\t\t}");
			}
			createModelBodyBuilder.AppendLine();
		}

		// --- CreateJson 本体（プロパティを DTO に変換する部分） ---
		var createJsonLinesBuilder = new StringBuilder();
		foreach (var p in props) {
			var strategy = p.Strategy;
			string jsonValueExpr;
			if (strategy.Kind == AccessKind.Collection) {
				// コレクション型: Select + ToArray で配列に変換
				jsonValueExpr = BuildCollectionToJsonExpr(strategy.ElementType, "model." + p.Name);
			} else {
				// 単一値型: 戦略の GetterExpr で値を取り出し、必要なら ForJson に変換
				var rawExpr = strategy.GetterExpr("model." + p.Name);
				jsonValueExpr = BuildElementToJsonExpr(strategy.ElementType, rawExpr);
			}
			createJsonLinesBuilder.AppendLine($"\t\t\t{p.Name} = {jsonValueExpr},");
		}

		// --- $id / $ref メタデータプロパティ（継承元がない場合のみ自己宣言） ---
		var metadataProps = string.IsNullOrEmpty(inheritance)
			? "\t\t/// <summary>JSON 参照 ($id) 用 ID。</summary>\n"
			  + "\t\tpublic string? ___Id { get; set; }\n\n"
			  + "\t\t/// <summary>JSON 参照 ($ref) 用参照文字列。</summary>\n"
			  + "\t\tpublic string? ___Ref { get; set; }"
			: "";

		// --- 派生型の場合: ModuleInitializer で ForJsonConverterRegistry に登録するコード ---
		var registrationCode = this.BuildRegistrationCode(modelSymbol, modelFullName, dtoFullName);

		// --- CreateModel / CreateModelCore のシグネチャ決定 ---
		// 継承なし → public static CreateModel
		// 継承あり → protected override CreateModelCore
		var createModelCoreVisibility = string.IsNullOrEmpty(inheritance) ? "public" : "protected override";
		var resolveMethod = string.IsNullOrEmpty(inheritance) ? "CreateModel" : "CreateModelCore";
		var resolveBaseTypeFqn = string.IsNullOrEmpty(inheritance)
			? modelFullName
			: this.FindGenerateJsonDtoBaseType(modelSymbol) ?? string.Empty;

		var startMethodModifiers = BuildCreateModelSignature(
			inheritance, createModelCoreVisibility, resolveBaseTypeFqn, resolveMethod, dtoFullName, modelFullName);

		// --- 全体を StringBuilder で組み立て ---
		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated />");
		sb.AppendLine("#nullable enable");
		sb.AppendLine();
		if (!string.IsNullOrEmpty(namespaceLine)) {
			sb.AppendLine(namespaceLine);
		}

		sb.AppendLine($"public partial class {modelSymbol.Name}ForJson{inheritance} {{");
		sb.AppendLine(metadataProps);
		sb.AppendLine();
		sb.Append(propLinesBuilder);
		sb.AppendLine(startMethodModifiers);
		sb.AppendLine($"\t\tvar model = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{modelFullName}>(sp);");
		sb.AppendLine("\t\tif (json.___Id is { } id) resolver.Add(id, model);");
		sb.Append(createModelBodyBuilder);
		sb.AppendLine("\t\treturn model;");
		sb.AppendLine("\t}");
		sb.AppendLine();
		sb.AppendLine("\t/// <summary>モデルオブジェクトから JSON DTO を生成する。循環参照は $ref として出力する。</summary>");
		sb.AppendLine("\t[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(model))]");
		sb.AppendLine($"\tpublic static {dtoFullName}? CreateJson({modelFullName}? model, global::R3.JsonConfig.ReferenceTracker? tracker = null) {{");
		sb.AppendLine("\t\tif (model is null) {");
		sb.AppendLine("\t\t\treturn null;");
		sb.AppendLine("\t\t}");
		sb.AppendLine("\t\ttracker ??= new global::R3.JsonConfig.ReferenceTracker();");
		sb.AppendLine("\t\t// 既に追跡済みの場合は $ref のみの DTO を返して循環参照を防ぐ");
		sb.AppendLine("\t\tif (tracker.GetOrAddId(model) is { } id) {");
		sb.AppendLine($"\t\t\treturn new {dtoFullName} {{ ___Ref = id }};");
		sb.AppendLine("\t\t}");
		sb.AppendLine();
		sb.AppendLine("\t\treturn new() {");
		sb.AppendLine("\t\t\t___Id = tracker.GetId(model),");
		sb.Append(createJsonLinesBuilder);
		sb.AppendLine("\t\t};");
		sb.AppendLine("\t}");
		sb.AppendLine("}");
		if (!string.IsNullOrEmpty(registrationCode)) {
			sb.AppendLine(registrationCode);
		}

		return sb.ToString();
	}

	/// <summary>
	/// <c>CreateModel</c> または <c>CreateModelCore</c> のシグネチャ文字列を組み立てる。
	/// </summary>
	private static string BuildCreateModelSignature(
		string inheritance,
		string visibility,
		string resolveBaseTypeFqn,
		string resolveMethod,
		string dtoFullName,
		string modelFullName) {

		if (string.IsNullOrEmpty(inheritance)) {
			// トップレベル具象クラス: public static CreateModel(json, sp, resolver?)
			return
$@"	/// <summary>JSON DTO からモデルオブジェクトを復元する。</summary>
	[return: global::System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(json))]
	public static {modelFullName}? CreateModel({dtoFullName}? json, global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver? resolver = null) {{
		if(json is null){{
			return null;
		}}
		if (json.___Ref is {{ }} @ref)
			return resolver?.Resolve<{modelFullName}>(@ref)
				?? throw new global::System.InvalidOperationException($""Reference not found: {{@ref}}"");
		resolver ??= new global::R3.JsonConfig.ReferenceResolver();
";
		} else {
			// 派生クラス: protected override CreateModelCore(sp, resolver)
			return
$@"	/// <summary>基底型の CreateModelCore をオーバーライドし、この型のモデルを復元する。</summary>
	{visibility} {resolveBaseTypeFqn} {resolveMethod}(global::System.IServiceProvider sp, global::R3.JsonConfig.ReferenceResolver resolver) {{
		var json = this;
";
		}
	}

	/// <summary>
	/// 派生型の場合に <c>ForJsonConverterRegistry</c> へ登録する
	/// <c>ModuleInitializer</c> コードを生成する。
	/// 派生型でなければ空文字列を返す。
	/// </summary>
	private string BuildRegistrationCode(
		INamedTypeSymbol modelSymbol,
		string modelFullName,
		string dtoFullName) {

		var isDerived = modelSymbol.GetAttributes()
			.Any(a => a.AttributeClass?.ToDisplayString() == this.DerivedTypeAttributeName);
		if (!isDerived) {
			return "";
		}

		var inheritance = this.GetInheritance(modelSymbol);
		if (string.IsNullOrEmpty(inheritance)) {
			return "";
		}

		// 識別子文字列を取得
		var discriminator = "Unknown";
		foreach (var attr in modelSymbol.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() == this.DerivedTypeAttributeName
			 && attr.ConstructorArguments.Length == 1
			 && attr.ConstructorArguments[0].Value is string s) {
				discriminator = s;
				break;
			}
		}

		var baseTypeFullName = this.FindGenerateJsonDtoBaseType(modelSymbol);
		if (baseTypeFullName is null) {
			return "";
		}

		return
$@"
/// <summary>
/// <c>[ModuleInitializer]</c> で <c>ForJsonConverterRegistry</c> に派生型 DTO を登録する。
/// 自動生成コード。手動で呼び出す必要はない。
/// </summary>
internal static partial class {modelSymbol.Name}ForJsonRuntimeRegistration {{
	[global::System.Runtime.CompilerServices.ModuleInitializer]
	public static void Register() {{
		global::R3.JsonConfig.ForJsonConverterRegistry.Register<
			{baseTypeFullName},
			{baseTypeFullName}ForJson,
			{modelFullName},
			{dtoFullName}>(""{discriminator}"", (m, t) => {modelSymbol.Name}ForJson.CreateJson(m, t));
	}}
}}";
	}

	// --------------------------------------------------------
	// 式生成ヘルパー
	// --------------------------------------------------------

	/// <summary>
	/// JSON DTO の単一要素値をモデルに変換する式を返す。
	/// 要素型に <see cref="TargetAttribute"/> が付いている場合は対応する
	/// <c>ForJson.CreateModel</c> を呼び出す式を生成する。
	/// </summary>
	/// <param name="elementType">要素の型シンボル。</param>
	/// <param name="createScope">子を独立した DI スコープで生成するかどうか。</param>
	/// <param name="inputExpr">変換対象の値式（例: <c>"e"</c>）。</param>
	private static string BuildElementToModelExpr(ITypeSymbol elementType, bool createScope, string inputExpr) {
		if (elementType is INamedTypeSymbol elemNamed
		 && elemNamed.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString()
			== "R3.JsonConfig.Attributes.GenerateR3JsonConfigDtoAttribute")) {
			var dtoTypeFqn = elemNamed.ToDisplayString(FullyQualifiedFormat).TrimEnd('?') + "ForJson";
			if (createScope) {
				// [JsonConfigCreateScope] が付いている場合は子 DI スコープを生成してモデル復元
				return $"{dtoTypeFqn}.CreateModel({inputExpr}, "
					 + "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(sp).ServiceProvider, resolver)";
			}
			return $"{dtoTypeFqn}.CreateModel({inputExpr}, sp, resolver)";
		}

		// DTO 生成対象でなければそのまま返す（プリミティブ等）
		return inputExpr;
	}

	/// <summary>
	/// モデルの単一要素値を JSON DTO に変換する式を返す。
	/// 要素型に <see cref="TargetAttribute"/> が付いている場合は対応する
	/// <c>ForJson.CreateJson</c> を呼び出す式を生成する。
	/// </summary>
	/// <param name="elementType">要素の型シンボル。</param>
	/// <param name="rawExpr">変換対象の値式（例: <c>"model.Prop.Value"</c>）。</param>
	private static string BuildElementToJsonExpr(ITypeSymbol elementType, string rawExpr) {
		if (elementType is INamedTypeSymbol elemNamed
		 && elemNamed.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString()
			== "R3.JsonConfig.Attributes.GenerateR3JsonConfigDtoAttribute")) {
			var dtoTypeFqn = elemNamed.ToDisplayString(FullyQualifiedFormat).TrimEnd('?') + "ForJson";
			return $"{dtoTypeFqn}.CreateJson({rawExpr}, tracker)";
		}

		return rawExpr;
	}

	/// <summary>
	/// モデルのコレクションプロパティ全体を JSON DTO の配列に変換する式を返す。
	/// 要素型に <see cref="TargetAttribute"/> が付いている場合は各要素を
	/// <c>ForJson.CreateJson</c> で変換した後に <c>ToArray</c> する式を生成する。
	/// </summary>
	/// <param name="elementType">コレクションの要素型シンボル。</param>
	/// <param name="propExpr">コレクションプロパティのアクセス式（例: <c>"model.Items"</c>）。</param>
	private static string BuildCollectionToJsonExpr(ITypeSymbol elementType, string propExpr) {
		if (elementType is INamedTypeSymbol elemNamed
		 && elemNamed.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString()
			== "R3.JsonConfig.Attributes.GenerateR3JsonConfigDtoAttribute")) {
			var dtoTypeFqn = elemNamed.ToDisplayString(FullyQualifiedFormat).TrimEnd('?') + "ForJson";
			return $"global::System.Linq.Enumerable.ToArray("
				 + $"global::System.Linq.Enumerable.Select({propExpr}, x => {dtoTypeFqn}.CreateJson(x, tracker)))";
		}

		// 要素が DTO 対象でなければ単純に ToArray
		return $"global::System.Linq.Enumerable.ToArray({propExpr})";
	}
}