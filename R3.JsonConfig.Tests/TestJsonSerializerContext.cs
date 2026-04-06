using System.Text.Json.Serialization;

using R3.JsonConfig.Demo.Composition.Store;

namespace R3.JsonConfig.Tests;

[JsonSourceGenerationOptions(
	WriteIndented = true,
	Converters = [typeof(ColorJsonConverter)]
)]
[JsonSerializable(typeof(ParentModelForJson))]
[JsonSerializable(typeof(IPluginConfigForJson))]
[JsonSerializable(typeof(FilePluginConfigForJson))]
[JsonSerializable(typeof(HttpPluginConfigForJson))]
public partial class TestJsonSerializerContext : JsonSerializerContext {
	public static readonly System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver ModifiableDefault = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver { Modifiers = { global::R3.JsonConfig.ForJsonConverterRegistry.ApplyPolymorphism } };
}