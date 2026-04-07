using R3.JsonConfig.Attributes;
using R3.JsonConfig.Demo.Composition.Store;

namespace R3.JsonConfig.Demo.Entity1;

/// <summary>
/// ファイルベースのプラグイン設定。
/// </summary>
[GenerateR3JsonConfigDto]
[JsonConfigDerivedType("File")]
public class FilePluginConfig : IPluginConfig {
	/// <summary>
	/// ファイルのパス。
	/// </summary>
	public string? FilePath {
		get; set;
	}
}