using GenJsonConfig.Attributes;
using GenJsonConfig.Demo.Composition.Store;

namespace GenJsonConfig.Demo.Entity1;

/// <summary>
/// ファイルベースのプラグイン設定。
/// </summary>
[GenerateJsonConfigDto]
[JsonConfigDerivedType("File")]
public class FilePluginConfig : IPluginConfig {
	/// <summary>
	/// ファイルのパス。
	/// </summary>
	public string? FilePath {
		get; set;
	}
}