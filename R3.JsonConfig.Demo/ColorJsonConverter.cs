using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace R3.JsonConfig.Demo;

public class ColorJsonConverter : JsonConverter<Color?> {
	public override Color? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		var hex = reader.GetString();

		if (string.IsNullOrWhiteSpace(hex)) {
			return null;
		}
		hex = hex.Trim();
		if (hex.StartsWith('#')) {
			hex = hex[1..];
		}
		if (hex.Length != 8) {
			return Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
		}

		var a = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
		var r = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber);
		var g = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
		var b = byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber);
		return Color.FromArgb(a, r, g, b);
	}

	public override void Write(Utf8JsonWriter writer, Color? value, JsonSerializerOptions options) {
		if (value is not Color c) {
			return;
		}
		writer.WriteStringValue($"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}");
	}
}