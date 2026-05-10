using System.Text.Json;
using GenJsonConfig.Attributes;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using R3;
using Shouldly;
using Xunit;

namespace GenJsonConfig.Tests;

[GenerateJsonConfigDto]
public class CircularParent {
	public string Name { get; set; } = "";
	public CircularChild? Child {
		get; set;
	}
}

[GenerateJsonConfigDto]
public class CircularChild {
	public string Name { get; set; } = "";
	public CircularParent? Parent {
		get; set;
	}
}

[GenerateJsonConfigDto]
public class DiamondRoot {
	public DiamondNode? Node1 {
		get; set;
	}
	public DiamondNode? Node2 {
		get; set;
	}
}

[GenerateJsonConfigDto]
public class DiamondNode {
	public string Name { get; set; } = "";
}

[GenerateJsonConfigDto]
public class CollectionCircularRoot {
	public ObservableList<CollectionCircularItem> Items { get; } = new();
}

[GenerateJsonConfigDto]
public class CollectionCircularItem {
	public CollectionCircularRoot? Root {
		get; set;
	}
}

public class CircularReferenceE2ETest {
	private static IServiceProvider CreateServiceProvider() {
		var services = new ServiceCollection();
		services.AddTransient<CircularParent>();
		services.AddTransient<CircularChild>();
		services.AddTransient<DiamondRoot>();
		services.AddTransient<DiamondNode>();
		services.AddTransient<CollectionCircularRoot>();
		services.AddTransient<CollectionCircularItem>();
		return services.BuildServiceProvider();
	}

	[Fact]
	public void RoundTrip_MutualReference_PreservesIdentity() {
		var sp = CreateServiceProvider();
		var parent = new CircularParent { Name = "Parent" };
		var child = new CircularChild { Name = "Child" };
		parent.Child = child;
		child.Parent = parent;

		var dto = CircularParentForJson.CreateJson(parent);
		var json = JsonSerializer.Serialize(dto);

		json.ShouldContain("\"___Id\"");
		json.ShouldContain("\"___Ref\"");

		var deserializedDto = JsonSerializer.Deserialize<CircularParentForJson>(json);
		var restoredParent = CircularParentForJson.CreateModel(deserializedDto, sp);

		restoredParent.ShouldNotBeNull();
		restoredParent.Name.ShouldBe("Parent");
		restoredParent.Child.ShouldNotBeNull();
		restoredParent.Child.Name.ShouldBe("Child");
		restoredParent.Child.Parent.ShouldBeSameAs(restoredParent);
	}

	[Fact]
	public void RoundTrip_DiamondReference_PreservesIdentity() {
		var sp = CreateServiceProvider();
		var node = new DiamondNode { Name = "Shared" };
		var root = new DiamondRoot {
			Node1 = node,
			Node2 = node
		};

		var dto = DiamondRootForJson.CreateJson(root);
		var json = JsonSerializer.Serialize(dto);

		var deserializedDto = JsonSerializer.Deserialize<DiamondRootForJson>(json);
		var restoredRoot = DiamondRootForJson.CreateModel(deserializedDto, sp);

		restoredRoot.ShouldNotBeNull();
		restoredRoot.Node1.ShouldNotBeNull();
		restoredRoot.Node2.ShouldNotBeNull();
		restoredRoot.Node1.ShouldBeSameAs(restoredRoot.Node2);
		restoredRoot.Node1.Name.ShouldBe("Shared");
	}

	[Fact]
	public void RoundTrip_CollectionCircularReference_PreservesIdentity() {
		var sp = CreateServiceProvider();
		var root = new CollectionCircularRoot();
		var item = new CollectionCircularItem { Root = root };
		root.Items.Add(item);

		var dto = CollectionCircularRootForJson.CreateJson(root);
		var json = JsonSerializer.Serialize(dto);

		var deserializedDto = JsonSerializer.Deserialize<CollectionCircularRootForJson>(json);
		var restoredRoot = CollectionCircularRootForJson.CreateModel(deserializedDto, sp);

		restoredRoot.ShouldNotBeNull();
		restoredRoot.Items.Count.ShouldBe(1);
		restoredRoot.Items[0].Root.ShouldBeSameAs(restoredRoot);
	}
}