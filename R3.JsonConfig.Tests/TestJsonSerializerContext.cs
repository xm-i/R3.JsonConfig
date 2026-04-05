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
}