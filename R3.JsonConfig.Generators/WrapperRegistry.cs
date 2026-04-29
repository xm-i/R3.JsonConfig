using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace R3.JsonConfig.Generators;

/// <summary>
/// ジェネレータが使用するラッパー型の登録情報。
/// オープンジェネリックの wrapper 型と adapter 型の対を保持する。
/// </summary>
internal sealed class WrapperEntry {
	/// <summary>
	/// ラッパー型のオープンジェネリック定義（ConstructedFrom で比較する）。
	/// null の場合は組み込みエントリ（文字列ベースの MetadataName 比較で照合）。
	/// </summary>
	public INamedTypeSymbol? WrapperSymbol {
		get;
	}

	/// <summary>
	/// 組み込みエントリの MetadataName（例: "ReactiveProperty`1"）。
	/// WrapperSymbol が null の場合のみ使用。
	/// </summary>
	public string? BuiltinMetadataName {
		get;
	}

	/// <summary>
	/// 組み込みエントリの名前空間（MetadataName と組み合わせて照合）。
	/// </summary>
	public string? BuiltinNamespace {
		get;
	}

	/// <summary>
	/// アダプター型のオープンジェネリック定義シンボル。
	/// null の場合は組み込みエントリ（GetterTemplate / SetterTemplate を使用）。
	/// </summary>
	public INamedTypeSymbol? AdapterSymbol {
		get;
	}

	/// <summary>
	/// 組み込みエントリ用の getter テンプレート。
	/// {0} = wrapper プロパティのアクセス式（例: "model.Prop"）。
	/// </summary>
	public string? GetterTemplate {
		get;
	}

	/// <summary>
	/// 組み込みエントリ用の setter テンプレート。
	/// {0} = wrapper プロパティのアクセス式、{1} = セットする値式。
	/// </summary>
	public string? SetterTemplate {
		get;
	}

	/// <summary>組み込みエントリ（MetadataName ベース）を作成する。</summary>
	public WrapperEntry(string builtinMetadataName, string builtinNamespace, string getterTemplate, string setterTemplate) {
		this.BuiltinMetadataName = builtinMetadataName;
		this.BuiltinNamespace = builtinNamespace;
		this.GetterTemplate = getterTemplate;
		this.SetterTemplate = setterTemplate;
	}

	/// <summary>アセンブリ属性から登録されたエントリを作成する。</summary>
	public WrapperEntry(INamedTypeSymbol wrapperSymbol, INamedTypeSymbol adapterSymbol) {
		this.WrapperSymbol = wrapperSymbol;
		this.AdapterSymbol = adapterSymbol;
	}
}

/// <summary>
/// ラッパー型登録の管理クラス。
/// 組み込みデフォルト（ReactiveProperty）と、アセンブリ属性 [RegisterJsonConfigWrapper] による登録を統合する。
/// </summary>
internal sealed class WrapperRegistry {
	private static readonly string RegisterWrapperAttributeFqn = "R3.JsonConfig.Attributes.RegisterJsonConfigWrapperAttribute";

	private readonly List<WrapperEntry> _entries;

	private WrapperRegistry(List<WrapperEntry> entries) {
		this._entries = entries;
	}

	/// <summary>
	/// コンパイル情報から WrapperRegistry を構築する。
	/// 組み込みデフォルトを先頭に追加し、アセンブリ属性による登録を後続に追加する。
	/// </summary>
	public static WrapperRegistry Build(Compilation compilation) {
		var entries = new List<WrapperEntry>();

		// 組み込みデフォルト: ReactiveProperty<T> (.Value getter/setter)
		entries.Add(new WrapperEntry(
			builtinMetadataName: "ReactiveProperty`1",
			builtinNamespace: "R3",
			getterTemplate: "{0}.Value",
			setterTemplate: "{0}.Value = {1};"
		));

		// アセンブリ属性から追加登録を収集（自アセンブリ + 参照アセンブリ）
		CollectFromAssembly(compilation.Assembly, entries);
		foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols) {
			CollectFromAssembly(refAsm, entries);
		}

		return new WrapperRegistry(entries);
	}

	private static void CollectFromAssembly(IAssemblySymbol assembly, List<WrapperEntry> entries) {
		foreach (var attr in assembly.GetAttributes()) {
			if (attr.AttributeClass?.ToDisplayString() != RegisterWrapperAttributeFqn) {
				continue;
			}
			if (attr.ConstructorArguments.Length != 2) {
				continue;
			}
			if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol wrapperTypeArg || attr.ConstructorArguments[1].Value is not INamedTypeSymbol adapterTypeArg) {
				continue;
			}

			// OriginalDefinition = オープンジェネリック定義
			entries.Add(new WrapperEntry(wrapperTypeArg.OriginalDefinition, adapterTypeArg.OriginalDefinition));
		}
	}

	/// <summary>
	/// 指定した型シンボルに対するラッパーエントリを返す。
	/// 組み込みエントリ（MetadataName 照合）→ アセンブリ属性登録（ConstructedFrom 照合）の順で検索する。
	/// </summary>
	public bool TryGetEntry(INamedTypeSymbol typeSymbol, out WrapperEntry? entry) {
		foreach (var e in this._entries) {
			if (e.BuiltinMetadataName is { } metaName) {
				// 組み込みエントリ: MetadataName + 名前空間で照合
				if (typeSymbol.MetadataName == metaName &&
					typeSymbol.ContainingNamespace?.ToDisplayString() == e.BuiltinNamespace) {
					entry = e;
					return true;
				}
			} else if (e.WrapperSymbol is { } wrapperSymbol) {
				// アセンブリ属性エントリ: ConstructedFrom で照合
				if (SymbolEqualityComparer.Default.Equals(typeSymbol.OriginalDefinition, wrapperSymbol)) {
					entry = e;
					return true;
				}
			}
		}

		entry = null;
		return false;
	}
}