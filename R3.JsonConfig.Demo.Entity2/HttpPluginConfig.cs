using R3.JsonConfig.Attributes;
using R3.JsonConfig.Demo.Composition.Store;

namespace R3.JsonConfig.Demo.Entity2;

[GenerateR3JsonConfigDto]
[JsonConfigDerivedType("Http")]
public class HttpPluginConfig : IPluginConfig {
	public string Url { get; set; } = "https://example.com";
}