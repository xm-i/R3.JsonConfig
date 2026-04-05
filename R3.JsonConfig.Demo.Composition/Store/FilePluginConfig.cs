using R3.JsonConfig.Attributes;

namespace R3.JsonConfig.Demo.Composition.Store;

[GenerateR3JsonConfigDto]
public class FilePluginConfig : IPluginConfig {
	public string FilePath { get; set; } = "config.txt";
}