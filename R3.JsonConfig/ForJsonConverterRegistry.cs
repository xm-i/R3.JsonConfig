using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;

namespace R3.JsonConfig;

/// <summary>
/// 実行時に、ドメインモデルから DTO への変換を行う関数を管理するレジストリ。
/// 複数プロジェクトにまたがるポリモーフィズムを実現するために使用されます。
/// </summary>
public static class ForJsonConverterRegistry {
	private sealed class Entry {
		public Func<object, ReferenceTracker, object> CreateJsonFunc {
			get;
		}
		public string Discriminator {
			get;
		}
		public Type DtoType {
			get;
		}

		public Entry(Func<object, ReferenceTracker, object> createJsonFunc, string discriminator, Type dtoType) {
			this.CreateJsonFunc = createJsonFunc;
			this.Discriminator = discriminator;
			this.DtoType = dtoType;
		}
	}

	// (BaseModelType, BaseDtoType) -> (DerivedModelType -> Entry)
	private static readonly ConcurrentDictionary<(Type BaseModel, Type BaseDto), ConcurrentDictionary<Type, Entry>> registry = new();

	/// <summary>
	/// 特定の基底型に対する派生型の変換関数およびメタデータを登録します。
	/// </summary>
	public static void Register<TBaseModel, TBaseDto>(Type derivedModelType, Func<object, ReferenceTracker, object> createJson, string discriminator, Type derivedDtoType) {
		var key = (typeof(TBaseModel), typeof(TBaseDto));
		var baseModelDict = registry.GetOrAdd(key, _ => new ConcurrentDictionary<Type, Entry>());
		baseModelDict[derivedModelType] = new Entry(createJson, discriminator, derivedDtoType);
	}

	/// <summary>
	/// 登録されている変換関数を用いて、モデルを DTO に変換します。
	/// </summary>
	public static TBaseDto? CreateJson<TBaseModel, TBaseDto>(TBaseModel? model, ReferenceTracker tracker) where TBaseDto : class {
		if (model is null) {
			return null;
		}

		var key = (typeof(TBaseModel), typeof(TBaseDto));
		if (registry.TryGetValue(key, out var baseModelDict)) {
			var modelType = model.GetType();
			if (baseModelDict.TryGetValue(modelType, out var entry)) {
				return (TBaseDto)entry.CreateJsonFunc(model, tracker);
			}
		}

		throw new InvalidOperationException($"No implementation registered for type {model.GetType().FullName} derived from {typeof(TBaseModel).FullName}.");
	}

	/// <summary>
	/// System.Text.Json のシリアライズ時に、登録された派生型情報を動的に設定するためのモディファイアです。
	/// </summary>
	public static void ApplyPolymorphism(JsonTypeInfo typeInfo) {
		// typeInfo.Type が BaseDtoType と一致するものを検索
		foreach (var kvp in registry) {
			if (kvp.Key.BaseDto == typeInfo.Type) {
				var polyOptions = typeInfo.PolymorphismOptions ?? new System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions {
					TypeDiscriminatorPropertyName = "___Type"
				};

				foreach (var entry in kvp.Value.Values) {
					// 既に登録されているか確認
					var exists = false;
					if (polyOptions.DerivedTypes != null) {
						foreach (var derived in polyOptions.DerivedTypes) {
							if (derived.DerivedType == entry.DtoType) {
								exists = true;
								break;
							}
						}
					}

					if (!exists && polyOptions.DerivedTypes != null) {
						polyOptions.DerivedTypes.Add(new System.Text.Json.Serialization.Metadata.JsonDerivedType(entry.DtoType, entry.Discriminator));
					}
				}

				typeInfo.PolymorphismOptions = polyOptions;
				break;
			}
		}
	}
}