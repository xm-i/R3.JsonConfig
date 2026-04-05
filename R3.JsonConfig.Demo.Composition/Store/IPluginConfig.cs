using R3.JsonConfig.Attributes;

namespace R3.JsonConfig.Demo.Composition.Store;

[GenerateR3JsonConfigDto]
[JsonConfigDerivedType(typeof(FilePluginConfig), "File")]
[JsonConfigDerivedType(typeof(HttpPluginConfig), "Http")]
public interface IPluginConfig {
}