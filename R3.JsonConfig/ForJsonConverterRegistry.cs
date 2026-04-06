using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace R3.JsonConfig;

/// <summary>
/// ポリモーフィックな型のマッピング情報を実行時に管理するレジストリ。
/// </summary>
public static class ForJsonConverterRegistry {
	// キー: 基底 DTO 型（例: IPluginConfigForJson）
	// 値: その基底 DTO に紐づく派生 DTO 登録情報
	private static readonly Dictionary<Type, BaseRegistry> _registries = new();

	/// <summary>
	/// 基底 DTO 型に対応するレジストリを取得します。
	/// 未作成の場合は初回アクセス時に生成します。
	/// </summary>
	private static BaseRegistry GetOrAddRegistry(Type baseJsonType) {
		lock (_registries) {
			if (!_registries.TryGetValue(baseJsonType, out var registry)) {
				registry = new BaseRegistry();
				_registries.Add(baseJsonType, registry);
			}
			return registry;
		}
	}

	/// <summary>
	/// System.Text.Json の TypeInfo モディファイア。
	/// 実行時レジストリに登録済みの派生 DTO 情報を JsonPolymorphismOptions に反映します。
	/// </summary>
	public static void ApplyPolymorphism(JsonTypeInfo jsonTypeInfo) {
		// オブジェクト以外（配列・プリミティブ等）にはポリモーフィズム設定を適用しない。
		if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object) {
			return;
		}

		BaseRegistry? registry;
		lock (_registries) {
			// 対応する基底 DTO が未登録の場合は何もしない。
			if (!_registries.TryGetValue(jsonTypeInfo.Type, out registry)) {
				return;
			}
		}

		jsonTypeInfo.PolymorphismOptions ??= new JsonPolymorphismOptions {
			TypeDiscriminatorPropertyName = "___Type",
			UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization
		};

		foreach (var dt in registry.DerivedTypes) {
			jsonTypeInfo.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(dt.DerivedJsonType, dt.TypeDiscriminator));
		}
	}

	/// <summary>
	/// 派生モデル/派生 DTO の対応を実行時レジストリへ登録します。
	/// 生成コードの ModuleInitializer から呼び出されることを想定しています。
	/// </summary>
	public static void Register<TBaseModel, TBaseJson, TDerivedModel, TDerivedJson>(
		string typeDiscriminator,
		Func<TDerivedModel, ReferenceTracker, TBaseJson> createJsonDelegate)
		where TBaseJson : class
		where TDerivedModel : TBaseModel {
		var registry = GetOrAddRegistry(typeof(TBaseJson));
		registry.Register(
			new DerivedTypeEntry(typeof(TDerivedModel), typeof(TDerivedJson), typeDiscriminator),
			(model, tracker) => {
				if (model is TDerivedModel derivedModel) {
					return createJsonDelegate(derivedModel, tracker);
				}
				throw new InvalidOperationException($"Expected model of type {typeof(TDerivedModel).FullName} but got {model?.GetType().FullName}");
			});
	}

	/// <summary>
	/// 基底モデルから実際の派生型に応じた DTO 生成デリゲートを実行します。
	/// </summary>
	public static TBaseJson CreateJson<TBaseModel, TBaseJson>(TBaseModel? model, ReferenceTracker tracker)
		where TBaseJson : class {
		if (model is null) {
			return null!;
		}

		BaseRegistry? registry;
		lock (_registries) {
			if (!_registries.TryGetValue(typeof(TBaseJson), out registry)) {
				throw new InvalidOperationException($"No polymorphic types registered for base JSON type: {typeof(TBaseJson).FullName}");
			}
		}

		// 実行時型で厳密に解決し、対応する CreateJson を呼び出す。
		if (registry.TryGetCreateJsonDelegate(model.GetType(), out var createJson)) {
			return (TBaseJson)createJson!(model, tracker);
		}

		throw new InvalidOperationException($"Unknown derived type: {model.GetType().FullName}");
	}

	private sealed class BaseRegistry {
		private readonly List<DerivedTypeEntry> _derivedTypes = new();
		private readonly Dictionary<Type, Func<object, ReferenceTracker, object>> _createJsonDelegates = new();

		public IReadOnlyList<DerivedTypeEntry> DerivedTypes {
			get {
				return this._derivedTypes;
			}
		}

		public void Register(DerivedTypeEntry entry, Func<object, ReferenceTracker, object> createJsonDelegate) {
			lock (this._derivedTypes) {
				// 同一派生型の重複登録は無視して idempotent に扱う。
				if (this._createJsonDelegates.ContainsKey(entry.DerivedModelType)) {
					return;
				}
				this._derivedTypes.Add(entry);
				this._createJsonDelegates.Add(entry.DerivedModelType, createJsonDelegate);
			}
		}

		public bool TryGetCreateJsonDelegate(Type modelType, out Func<object, ReferenceTracker, object>? createJsonDelegate) {
			return this._createJsonDelegates.TryGetValue(modelType, out createJsonDelegate);
		}
	}

	private sealed class DerivedTypeEntry {
		public Type DerivedModelType {
			get;
		}
		public Type DerivedJsonType {
			get;
		}
		public string TypeDiscriminator {
			get;
		}

		public DerivedTypeEntry(Type derivedModelType, Type derivedJsonType, string typeDiscriminator) {
			this.DerivedModelType = derivedModelType;
			this.DerivedJsonType = derivedJsonType;
			this.TypeDiscriminator = typeDiscriminator;
		}
	}
}