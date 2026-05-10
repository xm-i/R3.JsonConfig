using GenJsonConfig.Attributes;
using GenJsonConfig.Demo.Composition.Store;

namespace GenJsonConfig.Demo.Entity2;

/// <summary>
/// HTTP ベースのプラグイン設定。
/// </summary>
[GenerateJsonConfigDto]
[JsonConfigDerivedType("Http")]
public class HttpPluginConfig : IPluginConfig {
	/// <summary>
	/// 接続先の URL。
	/// </summary>
	public string? Url {
		get; set;
	}
}