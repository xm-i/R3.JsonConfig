using R3.JsonConfig.Attributes;
using R3.JsonConfig.Demo.Composition.Store;

namespace R3.JsonConfig.Demo.Entity1;

[GenerateR3JsonConfigDto]
[JsonConfigDerivedType("File")]
public class FilePluginConfig : IPluginConfig {
	public string FilePath { get; set; } = "config.txt";
}