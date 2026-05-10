using Microsoft.CodeAnalysis;

using GenJsonConfig.Generators;

namespace GenJsonConfig.Demo.Generators;

[Generator]
public class JsonDtoGenerator: DefaultJsonDtoGenerator {
	protected override string TargetAttribute {
		get;
	} = "GenJsonConfig.Demo.OriginalDtoAttribute";

	public JsonDtoGenerator() {
		this.ConversionRules.Add(new (
			"System.Drawing.Color",
			JsonDtoType.Text,
			"GenJsonConfig.Demo.JsonUtils.ColorToHex",
			"GenJsonConfig.Demo.JsonUtils.HexToColor"
		));
	}
}
