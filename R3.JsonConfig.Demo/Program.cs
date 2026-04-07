using System.Drawing;
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

			// ReactiveProperties
			pm.StringRp.Value = "Full Demo String";
			pm.ColorRp.Value = Color.Magenta;
			pm.ChildRp.Value = new ChildModel { Name = "Primary Child" };
			pm.PluginRp.Value = new FilePluginConfig { FilePath = "plugin_rp.txt" };

			// ObservableLists
			pm.IntArray.Add(10);
			pm.IntArray.Add(20);
			pm.IntArray.Add(30);

			pm.ColorArray.Add(Color.Cyan);
			pm.ColorArray.Add(Color.Yellow);

			pm.ChildArray.Add(new ChildModel { Name = "Array Child 1" });
			pm.ChildArray.Add(new ChildModel { Name = "Array Child 2" });

			pm.PluginList.Add(new HttpPluginConfig { Url = "https://list.example.com" });
			pm.PluginList.Add(new FilePluginConfig { FilePath = "list_item.cfg" });

			// Plain Properties
			pm.StringProperty = "Regular Property Value";
			pm.ColorProperty = Color.Orange;
			pm.ChildProperty = new ChildModel { Name = "Direct Child Property" };

			// Polymorphic Properties
			pm.Plugin = new HttpPluginConfig { Url = "https://direct.example.com" };
			pm.Plugin2 = new FilePluginConfig { FilePath = "direct_plugin_2.txt" };

			// シリアライズ実行
			var json = JsonSerializer.Serialize(ParentModelForJson.CreateJson(pm), options);
			File.WriteAllText("config.json", json);
			Console.WriteLine("config.json has been created with all properties initialized.");
		}
	}
}