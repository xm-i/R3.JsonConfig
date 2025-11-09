namespace R3.JsonConfig.Generators;

public sealed class ConversionRule {
	public string TypeFullName {
		get;
	}

	public JsonDtoType DtoType {
		get;
	}

	public string ConverterMethodName {
		get;
	}

	public string InverterMethodName {
		get;
    }

	public ConversionRule(string typeFullName, JsonDtoType dtoType, string converterMethodName, string inverterMethodName) {
		this.TypeFullName = typeFullName;
		this.DtoType = dtoType;
		this.ConverterMethodName = converterMethodName;
		this.InverterMethodName = inverterMethodName;
	}
}