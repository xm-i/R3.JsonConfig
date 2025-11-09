namespace R3.JsonConfig.Generators; 
public static class JsonConfigUtil {
	public static string JsonDtoTypeToString(JsonDtoType dtoType) {
		return dtoType switch {
			JsonDtoType.Text => "string?",
			JsonDtoType.Number => "double?",
			JsonDtoType.Boolean => "bool?",
			_ => throw new ArgumentOutOfRangeException(nameof(dtoType), dtoType, null)
		};
	}
}
