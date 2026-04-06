using System.Text.Json;

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

		var pm = serviceProvider.GetRequiredService<ParentModel>();

		if (File.Exists("config.json")) {
			var json = File.ReadAllText("config.json");
			var model = JsonSerializer.Deserialize(json, ConfigJsonSerializerContext.Default.ParentModelForJson);
		} else {
			var json = JsonSerializer.Serialize(ParentModelForJson.CreateJson(pm), ConfigJsonSerializerContext.Default.ParentModelForJson);
			File.WriteAllText("config.json", json);
		}
	}
}