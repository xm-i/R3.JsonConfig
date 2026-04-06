using System.Text.Json.Serialization;

using R3.JsonConfig.Demo.Composition.Store;
using R3.JsonConfig.Demo.Entity1;
using R3.JsonConfig.Demo.Entity2;

namespace R3.JsonConfig.Demo;

[JsonSourceGenerationOptions(WriteIndented = true, Converters = [typeof(ColorJsonConverter)])]
[JsonSerializable(typeof(ParentModelForJson))]
[JsonSerializable(typeof(IPluginConfigForJson))]
[JsonSerializable(typeof(FilePluginConfigForJson))]
[JsonSerializable(typeof(HttpPluginConfigForJson))]
public partial class ConfigJsonSerializerContext : JsonSerializerContext {
}