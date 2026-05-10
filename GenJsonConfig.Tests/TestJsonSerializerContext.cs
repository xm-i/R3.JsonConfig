using System.Text.Json.Serialization;

using GenJsonConfig.Demo.Composition.Store;
using GenJsonConfig.Demo.Entity1;
using GenJsonConfig.Demo.Entity2;

namespace GenJsonConfig.Tests;

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