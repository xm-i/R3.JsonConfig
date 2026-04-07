using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.DependencyInjection;

using R3.JsonConfig.Demo;
using R3.JsonConfig.Demo.Entity1;
using R3.JsonConfig.Demo.Entity2;

public class Program {
	public static void Main(string[] args) {
		var serviceCollection = new ServiceCollection();
		serviceCollection.AddTransient<ParentModel>();
		serviceCollection.AddTransient<ChildModel>();
		serviceCollection.AddTransient<FilePluginConfig>();
		serviceCollection.AddTransient<HttpPluginConfig>();


		var serviceProvider = serviceCollection.BuildServiceProvider();

		// SourceGenerator で生成済みの既定設定を引き継ぎつつ、
		// 実行時レジストリのポリモーフィズム解決をモディファイアとして合成する。
		var options = new JsonSerializerOptions(ConfigJsonSerializerContext.Default.Options) {
			TypeInfoResolver = ConfigJsonSerializerContext.Default.WithAddedModifier(global::R3.JsonConfig.ForJsonConverterRegistry.ApplyPolymorphism)
		};

		if (File.Exists("config.json")) {
			var json = File.ReadAllText("config.json");
			var jsonModel = JsonSerializer.Deserialize<ParentModelForJson>(json, options);
			var model = ParentModelForJson.CreateModel(jsonModel, serviceProvider);
		} else {
			var pm = serviceProvider.GetRequiredService<ParentModel>();
			var json = JsonSerializer.Serialize(ParentModelForJson.CreateJson(pm), options);
			File.WriteAllText("config.json", json);
		}
	}
}