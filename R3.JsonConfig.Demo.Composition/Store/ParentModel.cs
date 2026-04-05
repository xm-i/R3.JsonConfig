using System.Drawing;

using ObservableCollections;

using R3.JsonConfig.Attributes;

namespace R3.JsonConfig.Demo.Composition.Store;

[GenerateR3JsonConfigDto]
public class ParentModel {

	public ReactiveProperty<string> StringRp {
		get;
	} = new("DefaultString");

	public ReactiveProperty<Color?> ColorRp {
		get;
	} = new(Color.Red);

	public ReactiveProperty<ChildModel> ChildRp {
		get;
	} = new();

	public ObservableList<int> IntArray {
		get;
	} = [0, 1, 2, 3];
	public ObservableList<Color?> ColorArray {
		get;
	} = [Color.Red, Color.Green];


	public ObservableList<ChildModel> ChildArray {
		get;
	} = [];

	public string StringProperty {
		get;
		set;
	} = "DefaultStringProperty";

	public Color? ColorProperty {
		get;
		set;
	} = Color.Blue;

	public ChildModel? ChildProperty {
		get;
		set;
	}

	public IPluginConfig? Plugin {
		get;
		set;
	} = new FilePluginConfig();

	public IPluginConfig? Plugin2 {
		get;
		set;
	} = new HttpPluginConfig();

	public ReactiveProperty<IPluginConfig> PluginRp {
		get;
	} = new(new FilePluginConfig());

	public ObservableList<IPluginConfig> PluginList {
		get;
	} = [];
}