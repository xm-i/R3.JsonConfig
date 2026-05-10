using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using GenJsonConfig.Demo.Composition.Store;
using GenJsonConfig.Demo.Entity1;
using GenJsonConfig.Demo.Entity2;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace GenJsonConfig.Tests;

public class JsonSerializationE2ETest {
	private static IServiceProvider CreateServiceProvider() {
		var services = new ServiceCollection();
		services.AddTransient<ParentModel>();
		services.AddTransient<ChildModel>();
		services.AddTransient<FilePluginConfig>();
		services.AddTransient<HttpPluginConfig>();
		return services.BuildServiceProvider();
	}

	private static JsonSerializerOptions CreateOptions() {
		var options = new JsonSerializerOptions(TestJsonSerializerContext.Default.Options);
		options.TypeInfoResolver = TestJsonSerializerContext.Default.WithAddedModifier(global::GenJsonConfig.ForJsonConverterRegistry.ApplyPolymorphism);
		return options;
	}

	[Fact]
	public void Serialize_DefaultModel_ContainsEmptyValues() {
		var model = new ParentModel();

		var forJson = ParentModelForJson.CreateJson(model);
		var json = JsonSerializer.Serialize(forJson, typeof(ParentModelForJson), CreateOptions());

		json.ShouldNotBeNullOrWhiteSpace();
		// 初期値が削除されたため、null として書き出される
		json.ShouldContain("\"StringRp\": null");
		json.ShouldContain("\"StringProperty\": null");
		json.ShouldContain("\"IntArray\": []");
	}

	[Fact]
	public void Serialize_ModelWithValues_ProducesCorrectJson() {
		var model = new ParentModel();
		model.StringRp.Value = "TestString";
		model.ColorRp.Value = Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
		model.StringProperty = "TestStringProp";
		model.ColorProperty = Color.FromArgb(0xFF, 0xAB, 0xCD, 0xEF);
		model.ChildRp.Value = new ChildModel { Name = "ReactiveChild" };
		model.ChildProperty = new ChildModel { Name = "DirectChild" };
		model.ChildArray.Add(new ChildModel { Name = "ArrayChild" });

		var forJson = ParentModelForJson.CreateJson(model);
		var json = JsonSerializer.Serialize(forJson, typeof(ParentModelForJson), CreateOptions());

		json.ShouldNotBeNullOrWhiteSpace();
		json.ShouldContain("\"StringRp\": \"TestString\"");
		json.ShouldContain("\"StringProperty\": \"TestStringProp\"");
		json.ShouldContain("#FF123456");
		json.ShouldContain("#FFABCDEF");
		json.ShouldContain("\"ReactiveChild\"");
		json.ShouldContain("\"DirectChild\"");
		json.ShouldContain("\"ArrayChild\"");
	}

	[Fact]
	public void Deserialize_FullJson_ProducesCorrectModel() {
		var json = """
		{
			"StringRp": "FromJson",
			"ColorRp": "#FF112233",
			"ChildRp": { "Name": "JsonChild" },
			"IntArray": [5, 10, 15],
			"ColorArray": ["#FFAABBCC"],
			"ChildArray": [{ "Name": "JsonArrayChild" }],
			"StringProperty": "JsonStringProp",
			"ColorProperty": "#FF445566",
			"ChildProperty": { "Name": "JsonDirectChild" }
		}
		""";
		var sp = CreateServiceProvider();

		var forJson = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		forJson.ShouldNotBeNull();
		var model = ParentModelForJson.CreateModel(forJson, sp);

		model.ShouldNotBeNull();
		model.StringRp.Value.ShouldBe("FromJson");
		model.ColorRp.Value.ShouldBe(Color.FromArgb(0xFF, 0x11, 0x22, 0x33));
		model.ChildRp.Value.ShouldNotBeNull();
		model.ChildRp.Value.Name.ShouldBe("JsonChild");

		model.IntArray.Count.ShouldBe(3);
		model.IntArray[0].ShouldBe(5);
		model.IntArray[1].ShouldBe(10);
		model.IntArray[2].ShouldBe(15);

		model.ColorArray.Count.ShouldBe(1);
		model.ColorArray[0].ShouldBe(Color.FromArgb(0xFF, 0xAA, 0xBB, 0xCC));

		model.ChildArray.Count.ShouldBe(1);
		model.ChildArray[0].Name.ShouldBe("JsonArrayChild");

		model.StringProperty.ShouldBe("JsonStringProp");
		model.ColorProperty.ShouldBe(Color.FromArgb(0xFF, 0x44, 0x55, 0x66));
		model.ChildProperty.ShouldNotBeNull();
		model.ChildProperty!.Name.ShouldBe("JsonDirectChild");
	}

	[Fact]
	public void Deserialize_PartialJson_OtherFieldsAreNull() {
		var json = """
		{
			"StringRp": "PartialOverride"
		}
		""";
		var sp = CreateServiceProvider();

		var forJson = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		forJson.ShouldNotBeNull();
		var model = ParentModelForJson.CreateModel(forJson, sp);

		model.ShouldNotBeNull();
		model.StringRp.Value.ShouldBe("PartialOverride");
		// 初期値がないため null になる
		model.StringProperty.ShouldBeNull();
		model.IntArray.Count.ShouldBe(0);
	}

	[Fact]
	public void RoundTrip_SerializeAndDeserialize_PreservesAllValues() {
		var sp = CreateServiceProvider();
		var original = new ParentModel();
		original.StringRp.Value = "RoundTrip";
		original.ColorRp.Value = Color.FromArgb(0xAA, 0xBB, 0xCC, 0xDD);
		original.StringProperty = "RoundTripProp";
		original.ColorProperty = Color.FromArgb(0x11, 0x22, 0x33, 0x44);
		original.ChildRp.Value = new ChildModel { Name = "RoundTripChild" };
		original.ChildProperty = new ChildModel { Name = "RoundTripDirectChild" };
		original.ChildArray.Add(new ChildModel { Name = "RoundTripArrayChild1" });
		original.ChildArray.Add(new ChildModel { Name = "RoundTripArrayChild2" });

		var forJson = ParentModelForJson.CreateJson(original);
		var json = JsonSerializer.Serialize(forJson, typeof(ParentModelForJson), CreateOptions());
		var deserialized = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		deserialized.ShouldNotBeNull();
		var restored = ParentModelForJson.CreateModel(deserialized, sp);

		restored.ShouldNotBeNull();
		restored.StringRp.Value.ShouldBe(original.StringRp.Value);
		restored.ColorRp.Value.ShouldBe(original.ColorRp.Value);
		restored.StringProperty.ShouldBe(original.StringProperty);
		restored.ColorProperty.ShouldBe(original.ColorProperty);
		restored.ChildRp.Value.ShouldNotBeNull();
		restored.ChildRp.Value.Name.ShouldBe("RoundTripChild");
		restored.ChildProperty.ShouldNotBeNull();
		restored.ChildProperty!.Name.ShouldBe("RoundTripDirectChild");

		restored.IntArray.Count.ShouldBe(original.IntArray.Count);
		for (var i = 0; i < original.IntArray.Count; i++) {
			restored.IntArray[i].ShouldBe(original.IntArray[i]);
		}

		restored.ColorArray.Count.ShouldBe(original.ColorArray.Count);
		for (var i = 0; i < original.ColorArray.Count; i++) {
			restored.ColorArray[i].GetValueOrDefault().ToArgb().ShouldBe(original.ColorArray[i].GetValueOrDefault().ToArgb());
		}

		restored.ChildArray.Count.ShouldBe(original.ChildArray.Count);
		for (var i = 0; i < original.ChildArray.Count; i++) {
			restored.ChildArray[i].Name.ShouldBe(original.ChildArray[i].Name);
		}
	}

	[Fact]
	public void Deserialize_EmptyJson_ProducesEmptyModel() {
		var json = "{}";
		var sp = CreateServiceProvider();

		var forJson = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		forJson.ShouldNotBeNull();
		var model = ParentModelForJson.CreateModel(forJson, sp);

		model.ShouldNotBeNull();
		model.StringRp.Value.ShouldBeNull();
		model.StringProperty.ShouldBeNull();
		model.IntArray.Count.ShouldBe(0);
		model.ColorArray.Count.ShouldBe(0);
	}

	[Fact]
	public void RoundTrip_ChildModel_PreservesName() {
		var sp = CreateServiceProvider();
		var child = new ChildModel { Name = "E2EChild" };

		var forJson = ChildModelForJson.CreateJson(child);
		forJson.ShouldNotBeNull();
		forJson.Name.ShouldBe("E2EChild");
		var restored = ChildModelForJson.CreateModel(forJson, sp);

		restored.ShouldNotBeNull();
		restored.Name.ShouldBe("E2EChild");
	}

	[Fact]
	public void CreateJson_AndCreateModel_ReturnNull_WhenInputIsNull() {
		var sp = CreateServiceProvider();

		ParentModelForJson.CreateJson(null).ShouldBeNull();
		ParentModelForJson.CreateModel(null, sp).ShouldBeNull();
		ChildModelForJson.CreateJson(null).ShouldBeNull();
		ChildModelForJson.CreateModel(null, sp).ShouldBeNull();
	}

	[Fact]
	public void Serialize_PolymorphicInterface_ContainsTypeDiscriminator() {
		IPluginConfig filePlugin = new FilePluginConfig { FilePath = "test.txt" };
		IPluginConfig httpPlugin = new HttpPluginConfig { Url = "https://test.com" };

		var fileDto = IPluginConfigForJson.CreateJson(filePlugin);
		var httpDto = IPluginConfigForJson.CreateJson(httpPlugin);

		var fileJson = JsonSerializer.Serialize(fileDto, typeof(IPluginConfigForJson), CreateOptions());
		var httpJson = JsonSerializer.Serialize(httpDto, typeof(IPluginConfigForJson), CreateOptions());

		fileJson.ShouldContain("\"___Type\": \"File\"");
		fileJson.ShouldContain("\"FilePath\": \"test.txt\"");

		httpJson.ShouldContain("\"___Type\": \"Http\"");
		httpJson.ShouldContain("\"Url\": \"https://test.com\"");
	}

	[Fact]
	public void Deserialize_PolymorphicJson_CreatesCorrectConcreteType() {
		var sp = CreateServiceProvider();
		var fileJson = """{"___Type": "File", "FilePath": "json.txt"}""";
		var httpJson = """{"___Type": "Http", "Url": "https://json.com"}""";

		var fileDto = (IPluginConfigForJson)JsonSerializer.Deserialize(fileJson, typeof(IPluginConfigForJson), CreateOptions())!;
		var httpDto = (IPluginConfigForJson)JsonSerializer.Deserialize(httpJson, typeof(IPluginConfigForJson), CreateOptions())!;

		var fileModel = IPluginConfigForJson.CreateModel(fileDto, sp);
		var httpModel = IPluginConfigForJson.CreateModel(httpDto, sp);

		fileModel.ShouldBeOfType<FilePluginConfig>();
		((FilePluginConfig)fileModel!).FilePath.ShouldBe("json.txt");

		httpModel.ShouldBeOfType<HttpPluginConfig>();
		((HttpPluginConfig)httpModel!).Url.ShouldBe("https://json.com");
	}

	[Fact]
	public void RoundTrip_NestedPolymorphicProperty_PreservesTypeAndValues() {
		var sp = CreateServiceProvider();
		var model = new ParentModel {
			Plugin = new HttpPluginConfig { Url = "https://roundtrip.com" },
			Plugin2 = new FilePluginConfig { FilePath = "roundtrip.txt" }
		};

		var dto = ParentModelForJson.CreateJson(model);
		var json = JsonSerializer.Serialize(dto, typeof(ParentModelForJson), CreateOptions());

		var deserializedDto = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		var restoredModel = ParentModelForJson.CreateModel(deserializedDto, sp);

		restoredModel.ShouldNotBeNull();
		restoredModel.Plugin.ShouldBeOfType<HttpPluginConfig>();
		((HttpPluginConfig)restoredModel.Plugin!).Url.ShouldBe("https://roundtrip.com");

		restoredModel.Plugin2.ShouldBeOfType<FilePluginConfig>();
		((FilePluginConfig)restoredModel.Plugin2!).FilePath.ShouldBe("roundtrip.txt");
	}

	[Fact]
	public void RoundTrip_ReactivePropertyOfPolymorphicInterface_PreservesTypeAndValues() {
		var sp = CreateServiceProvider();
		var model = new ParentModel();
		model.PluginRp.Value = new HttpPluginConfig { Url = "https://rp-roundtrip.com" };

		var dto = ParentModelForJson.CreateJson(model);
		var json = JsonSerializer.Serialize(dto, typeof(ParentModelForJson), CreateOptions());

		json.ShouldContain("\"___Type\": \"Http\"");
		json.ShouldContain("\"Url\": \"https://rp-roundtrip.com\"");

		var deserializedDto = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		var restoredModel = ParentModelForJson.CreateModel(deserializedDto, sp);

		restoredModel.ShouldNotBeNull();
		restoredModel.PluginRp.Value.ShouldBeOfType<HttpPluginConfig>();
		((HttpPluginConfig)restoredModel.PluginRp.Value).Url.ShouldBe("https://rp-roundtrip.com");
	}

	[Fact]
	public void RoundTrip_ObservableListOfPolymorphicInterface_PreservesTypeAndValues() {
		var sp = CreateServiceProvider();
		var model = new ParentModel();
		model.PluginList.Add(new FilePluginConfig { FilePath = "list1.txt" });
		model.PluginList.Add(new HttpPluginConfig { Url = "https://list2.com" });

		var dto = ParentModelForJson.CreateJson(model);
		var json = JsonSerializer.Serialize(dto, typeof(ParentModelForJson), CreateOptions());

		json.ShouldContain("\"___Type\": \"File\"");
		json.ShouldContain("\"FilePath\": \"list1.txt\"");
		json.ShouldContain("\"___Type\": \"Http\"");
		json.ShouldContain("\"Url\": \"https://list2.com\"");

		var deserializedDto = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		var restoredModel = ParentModelForJson.CreateModel(deserializedDto, sp);

		restoredModel.ShouldNotBeNull();
		restoredModel.PluginList.Count.ShouldBe(2);
		restoredModel.PluginList[0].ShouldBeOfType<FilePluginConfig>();
		((FilePluginConfig)restoredModel.PluginList[0]).FilePath.ShouldBe("list1.txt");
		restoredModel.PluginList[1].ShouldBeOfType<HttpPluginConfig>();
		((HttpPluginConfig)restoredModel.PluginList[1]).Url.ShouldBe("https://list2.com");
	}

	[Fact]
	public void Serialize_ReactivePropertyOfPolymorphicInterface_ContainsTypeDiscriminator() {
		var model = new ParentModel();
		model.PluginRp.Value = new FilePluginConfig { FilePath = "rp-plugin.txt" };

		var dto = ParentModelForJson.CreateJson(model);
		var json = JsonSerializer.Serialize(dto, typeof(ParentModelForJson), CreateOptions());

		json.ShouldContain("\"PluginRp\"");
		json.ShouldContain("\"___Type\": \"File\"");
		json.ShouldContain("\"FilePath\": \"rp-plugin.txt\"");
	}

	[Fact]
	public void Serialize_ObservableListOfPolymorphicInterface_ContainsTypeDiscriminators() {
		var model = new ParentModel();
		model.PluginList.Add(new FilePluginConfig { FilePath = "f1.txt" });
		model.PluginList.Add(new HttpPluginConfig { Url = "https://h1.com" });

		var dto = ParentModelForJson.CreateJson(model);
		var json = JsonSerializer.Serialize(dto, typeof(ParentModelForJson), CreateOptions());

		json.ShouldContain("\"PluginList\"");
		json.ShouldContain("\"___Type\": \"File\"");
		json.ShouldContain("\"FilePath\": \"f1.txt\"");
		json.ShouldContain("\"___Type\": \"Http\"");
		json.ShouldContain("\"Url\": \"https://h1.com\"");
	}

	[Fact]
	public void Deserialize_ReactivePropertyOfPolymorphicInterface_CreatesCorrectConcreteType() {
		var sp = CreateServiceProvider();
		var tempModel = new ParentModel();
		_ = JsonSerializer.Serialize(ParentModelForJson.CreateJson(tempModel), typeof(ParentModelForJson), CreateOptions());
		var json = """
		{
			"PluginRp": { "___Type": "Http", "Url": "https://deser-rp.com" }
		}
		""";

		var forJson = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		forJson.ShouldNotBeNull();
		var model = ParentModelForJson.CreateModel(forJson, sp);

		model.ShouldNotBeNull();
		model.PluginRp.Value.ShouldBeOfType<HttpPluginConfig>();
		((HttpPluginConfig)model.PluginRp.Value).Url.ShouldBe("https://deser-rp.com");
	}

	[Fact]
	public void Deserialize_ObservableListOfPolymorphicInterface_CreatesCorrectConcreteTypes() {
		var sp = CreateServiceProvider();
		var tempModel = new ParentModel();
		_ = JsonSerializer.Serialize(ParentModelForJson.CreateJson(tempModel), typeof(ParentModelForJson), CreateOptions());
		var json = """
		{
			"PluginList": [
				{ "___Type": "File", "FilePath": "deser1.txt" },
				{ "___Type": "Http", "Url": "https://deser2.com" }
			]
		}
		""";

		var forJson = (ParentModelForJson)JsonSerializer.Deserialize(json, typeof(ParentModelForJson), CreateOptions())!;
		forJson.ShouldNotBeNull();
		var model = ParentModelForJson.CreateModel(forJson, sp);

		model.ShouldNotBeNull();
		model.PluginList.Count.ShouldBe(2);
		model.PluginList[0].ShouldBeOfType<FilePluginConfig>();
		((FilePluginConfig)model.PluginList[0]).FilePath.ShouldBe("deser1.txt");
		model.PluginList[1].ShouldBeOfType<HttpPluginConfig>();
		((HttpPluginConfig)model.PluginList[1]).Url.ShouldBe("https://deser2.com");
	}
}