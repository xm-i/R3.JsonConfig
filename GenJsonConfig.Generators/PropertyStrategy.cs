using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace GenJsonConfig.Generators;

/// <summary>
/// プロパティのアクセス戦略の種類。
/// </summary>
internal enum AccessKind {
	/// <summary>通常のプロパティ（public setter への直接代入）。</summary>
	Plain,
	/// <summary>ラッパー型（ReactiveProperty 等）。</summary>
	Wrapped,
	/// <summary>コレクション型（IEnumerable&lt;T&gt; + Clear/Add）。</summary>
	Collection,
}

/// <summary>
/// プロパティ 1 件のコード生成に必要なアクセス戦略。
/// getter / setter の生成はデリゲートで表現し、変数名等の文脈依存を排除する。
/// </summary>
internal sealed class AccessStrategy {
	/// <summary>アクセスの種類。</summary>
	public AccessKind Kind {
		get;
	}

	/// <summary>
	/// 値を保持する要素の型シンボル。
	/// ラッパーの場合はラップされた型、コレクションの場合は要素型、Plain の場合はプロパティ型。
	/// </summary>
	public ITypeSymbol ElementType {
		get;
	}

	/// <summary>
	/// DTO 側プロパティの型名（例: "string?", "int[]?", "global::Ns.ChildForJson?"）。
	/// </summary>
	public string JsonPropertyType {
		get;
	}

	/// <summary>
	/// model→json 方向の getter 式。
	/// 引数: propExpr = モデル上のプロパティアクセス式（例: "model.Prop"）。
	/// 戻り値: 値を取り出す式（例: "model.Prop.Value"）。
	/// </summary>
	public Func<string, string> GetterExpr {
		get;
	}

	/// <summary>
	/// json→model 方向の setter 文。
	/// 引数: propExpr = モデル上のプロパティアクセス式、valueExpr = セットする値の式。
	/// 戻り値: 代入文（セミコロン付き）または Clear+foreach ブロック文字列。
	/// </summary>
	public Func<string, string, string> SetterStmt {
		get;
	}

	public AccessStrategy(
		AccessKind kind,
		ITypeSymbol elementType,
		string jsonPropertyType,
		Func<string, string> getterExpr,
		Func<string, string, string> setterStmt) {
		this.Kind = kind;
		this.ElementType = elementType;
		this.JsonPropertyType = jsonPropertyType;
		this.GetterExpr = getterExpr;
		this.SetterStmt = setterStmt;
	}
}

/// <summary>
/// 戦略解決のためのコンテキスト情報。
/// </summary>
internal sealed class ResolverContext {
	public WrapperRegistry WrapperRegistry {
		get;
	}

	public Func<INamedTypeSymbol, bool> HasGenerateJsonDtoAttribute {
		get;
	}

	public SymbolDisplayFormat FullyQualifiedFormat {
		get;
	}

	public ResolverContext(
		WrapperRegistry wrapperRegistry,
		Func<INamedTypeSymbol, bool> hasGenerateJsonDtoAttribute,
		SymbolDisplayFormat fullyQualifiedFormat) {
		this.WrapperRegistry = wrapperRegistry;
		this.HasGenerateJsonDtoAttribute = hasGenerateJsonDtoAttribute;
		this.FullyQualifiedFormat = fullyQualifiedFormat;
	}
}

/// <summary>
/// プロパティアクセス戦略のインターフェース。
/// </summary>
internal interface IPropertyStrategy {
	/// <summary>
	/// 指定プロパティに対して戦略を解決を試みる。
	/// </summary>
	/// <returns>解決できた場合は true。</returns>
	public bool TryResolve(IPropertySymbol prop, ResolverContext ctx, out AccessStrategy? strategy);
}

/// <summary>
/// ラッパー型（WrapperRegistry に登録されたもの）に対する戦略。
/// ReactiveProperty 等のオープンジェネリックラッパーを対象とする。
/// </summary>
internal sealed class WrapperPropertyStrategy : IPropertyStrategy {
	public bool TryResolve(IPropertySymbol prop, ResolverContext ctx, out AccessStrategy? strategy) {
		strategy = null;
		if (prop.Type is not INamedTypeSymbol nts) {
			return false;
		}
		if (!nts.IsGenericType || nts.TypeArguments.Length != 1) {
			return false;
		}
		if (!ctx.WrapperRegistry.TryGetEntry(nts, out var entry) || entry is null) {
			return false;
		}

		var innerType = nts.TypeArguments[0];
		string jsonPropType;
		string innerDisplay;

		if (innerType is INamedTypeSymbol innerNamed && ctx.HasGenerateJsonDtoAttribute(innerNamed)) {
			var d = innerNamed.ToDisplayString(ctx.FullyQualifiedFormat).TrimEnd('?');
			innerDisplay = d;
			jsonPropType = d + "ForJson?";
		} else {
			innerDisplay = innerType.ToDisplayString(ctx.FullyQualifiedFormat);
			jsonPropType = MakeNullable(innerDisplay);
		}

		// getter / setter の生成式を構築
		Func<string, string> getter;
		Func<string, string, string> setter;

		if (entry.GetterTemplate is { } getterTpl && entry.SetterTemplate is { } setterTpl) {
			// 組み込みエントリ
			getter = propExpr => getterTpl.Replace("{0}", propExpr);
			setter = (propExpr, valExpr) => setterTpl.Replace("{0}", propExpr).Replace("{1}", valExpr);
		} else if (entry.AdapterSymbol is { } adapterOpen) {
			// アセンブリ属性エントリ: アダプターをクローズドジェネリックに構築
			// アダプターの FQN を取得
			var adapterFqn = GetAdapterFqn(adapterOpen, innerType, ctx.FullyQualifiedFormat);
			var adapterFieldName = "__adapter_" + prop.Name;
			// アダプター経由のアクセス
			getter = propExpr => $"global::{adapterFqn}.Get({propExpr})";
			setter = (propExpr, valExpr) => $"global::{adapterFqn}.Set({propExpr}, {valExpr});";
			// Note: アダプターはステートレスなので毎回 new する（最適化は利用者側で）
			getter = propExpr => $"new global::{adapterFqn}().Get({propExpr})";
			setter = (propExpr, valExpr) => $"new global::{adapterFqn}().Set({propExpr}, {valExpr});";
		} else {
			return false;
		}

		strategy = new AccessStrategy(
			AccessKind.Wrapped,
			innerType,
			jsonPropType,
			getter,
			setter
		);
		return true;
	}

	private static string GetAdapterFqn(INamedTypeSymbol adapterOpen, ITypeSymbol typeArg, SymbolDisplayFormat fmt) {
		// Construct closed generic: AdapterOpen<typeArg>
		var constructed = adapterOpen.Construct(typeArg);
		return constructed.ToDisplayString(fmt).TrimStart('g').TrimStart('l').TrimStart('o').TrimStart('b').TrimStart('a').TrimStart('l').TrimStart(':').TrimStart(':');
		// 実際は ToDisplayString そのまま使う（global:: プレフィックスを除去しない）
	}

	private static string MakeNullable(string typeName) {
		return typeName.EndsWith("?") ? typeName : typeName + "?";
	}
}

/// <summary>
/// コレクション型（IEnumerable&lt;T&gt; + Add + Clear を持つ）に対する戦略。
/// ObservableList 等の任意のコレクション型を対象とする。
/// </summary>
internal sealed class CollectionPropertyStrategy : IPropertyStrategy {
	public bool TryResolve(IPropertySymbol prop, ResolverContext ctx, out AccessStrategy? strategy) {
		strategy = null;
		if (prop.Type is not INamedTypeSymbol nts) {
			return false;
		}
		// string は除外
		if (nts.SpecialType == SpecialType.System_String) {
			return false;
		}

		if (!TryGetCollectionElementType(nts, out var elementType) || elementType is null) {
			return false;
		}

		// Add / Clear メソッドが存在するか確認
		if (!HasAddAndClear(nts, elementType)) {
			return false;
		}

		string jsonItemType;
		if (elementType is INamedTypeSymbol elemNamed && ctx.HasGenerateJsonDtoAttribute(elemNamed)) {
			var d = elemNamed.ToDisplayString(ctx.FullyQualifiedFormat).TrimEnd('?');
			jsonItemType = d + "ForJson";
		} else {
			jsonItemType = elementType.ToDisplayString(ctx.FullyQualifiedFormat);
		}

		var jsonPropType = jsonItemType + "[]?";

		// getter: ToArray (or Select+ToArray for ForJson elements)
		Func<string, string> getter = propExpr => $"global::System.Linq.Enumerable.ToArray({propExpr})";

		// setter: Clear + foreach Add
		Func<string, string, string> setter = (propExpr, arrExpr) =>
			$$"""{{propExpr}}.Clear(); foreach (var __e in {{arrExpr}}) { {{propExpr}}.Add(__e); }""";

		strategy = new AccessStrategy(
			AccessKind.Collection,
			elementType,
			jsonPropType,
			getter,
			setter
		);
		return true;
	}

	internal static bool TryGetCollectionElementType(INamedTypeSymbol type, out ITypeSymbol? elementType) {
		foreach (var iface in type.AllInterfaces) {
			if (iface.IsGenericType &&
				iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) {
				elementType = iface.TypeArguments[0];
				return true;
			}
		}
		// 型自体が IEnumerable<T> の場合
		if (type.IsGenericType &&
			type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) {
			elementType = type.TypeArguments[0];
			return true;
		}
		elementType = null;
		return false;
	}

	private static bool HasAddAndClear(INamedTypeSymbol type, ITypeSymbol elementType) {
		var hasAdd = false;
		var hasClear = false;

		foreach (var member in type.GetMembers()) {
			if (member is IMethodSymbol method && method.DeclaredAccessibility == Accessibility.Public) {
				if (method.Name == "Clear" && method.Parameters.Length == 0) {
					hasClear = true;
				} else if (method.Name == "Add" && method.Parameters.Length == 1) {
					hasAdd = true;
				}
			}
		}

		// 基底クラスやインターフェースも確認
		if (!hasAdd || !hasClear) {
			foreach (var iface in type.AllInterfaces) {
				foreach (var member in iface.GetMembers()) {
					if (member is IMethodSymbol method) {
						if (method.Name == "Clear" && method.Parameters.Length == 0) {
							hasClear = true;
						} else if (method.Name == "Add" && method.Parameters.Length == 1) {
							hasAdd = true;
						}
					}
				}
			}
		}

		return hasAdd && hasClear;
	}
}

/// <summary>
/// 通常のプロパティ（public setter を持つ）に対する戦略。
/// </summary>
internal sealed class PlainPropertyStrategy : IPropertyStrategy {
	public bool TryResolve(IPropertySymbol prop, ResolverContext ctx, out AccessStrategy? strategy) {
		strategy = null;
		// public setter が必要
		if (prop.SetMethod is null || prop.SetMethod.DeclaredAccessibility != Accessibility.Public) {
			return false;
		}

		var typeSymbol = prop.Type;
		var display = typeSymbol.ToDisplayString(ctx.FullyQualifiedFormat);

		string jsonPropType;
		if (typeSymbol is INamedTypeSymbol nts && ctx.HasGenerateJsonDtoAttribute(nts)) {
			var nonNull = display.TrimEnd('?');
			jsonPropType = nonNull + "ForJson?";
		} else {
			jsonPropType = display.EndsWith("?") ? display : display + "?";
		}

		strategy = new AccessStrategy(
			AccessKind.Plain,
			typeSymbol,
			jsonPropType,
			propExpr => propExpr,
			(propExpr, valExpr) => $"{propExpr} = {valExpr};"
		);
		return true;
	}
}