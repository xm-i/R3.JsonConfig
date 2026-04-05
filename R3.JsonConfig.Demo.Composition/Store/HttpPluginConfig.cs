using R3.JsonConfig.Attributes;

namespace R3.JsonConfig.Demo.Composition.Store;

[GenerateR3JsonConfigDto]
[JsonConfigDerivedType("Http")]
public class HttpPluginConfig : IPluginConfig {
	public string Url { get; set; } = "https://example.com";
}