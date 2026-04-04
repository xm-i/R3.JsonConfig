using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using R3.JsonConfig.Demo;
using R3.JsonConfig.Demo.Composition.Store;

public class Program {
	public static void Main(string[] args) {
		var serviceCollection = new ServiceCollection();
		serviceCollection.AddTransient<ParentModel>();
		serviceCollection.AddTransient<ChildModel>();


		var serviceProvider = serviceCollection.BuildServiceProvider();

		var pm = serviceProvider.GetRequiredService<ParentModel>();

		if (File.Exists("config.json")) {
			var json = File.ReadAllText("config.json");
			JsonSerializer.Deserialize<ParentModelForJson>(json);
		} else {
			var json = JsonSerializer.Serialize(ParentModelForJson.CreateJson(pm), ConfigJsonSerializerContext.Default.ParentModelForJson);
			File.WriteAllText("config.json", json);
		}
	}
}