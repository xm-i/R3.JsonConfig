using R3.JsonConfig.Attributes;
using R3.JsonConfig.Demo.Composition.Store;

namespace R3.JsonConfig.Demo.Entity2;

/// <summary>
/// HTTP ベースのプラグイン設定。
/// </summary>
[GenerateR3JsonConfigDto]
[JsonConfigDerivedType("Http")]
public class HttpPluginConfig : IPluginConfig {
	/// <summary>
	/// 接続先の URL。
	/// </summary>
	public string? Url {
		get; set;
	}
}