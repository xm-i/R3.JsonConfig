using Microsoft.CodeAnalysis;

using R3.JsonConfig.Generators;

namespace R3.JsonConfig.Demo.Generators;

[Generator]
public class JsonDtoGenerator: DefaultJsonDtoGenerator {
	protected override string TargetAttribute {
		get;
	} = "R3.JsonConfig.Demo.OriginalDtoAttribute";

	public JsonDtoGenerator() {
		this.ConversionRules.Add(new (
			"System.Drawing.Color",
			JsonDtoType.Text,
			"R3.JsonConfig.Demo.JsonUtils.ColorToHex",
			"R3.JsonConfig.Demo.JsonUtils.HexToColor"
		));
	}
}
