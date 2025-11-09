using System.Drawing;

using ObservableCollections;

namespace R3.JsonConfig.Demo.Store;

[OriginalDto]
public class ParentModel {

	public ReactiveProperty<string> StringRp {
		get;
	} = new("DefaultString");

	public ReactiveProperty<Color?> ColorRp {
		get;
	} = new(Color.Red);

	public ReactiveProperty<ChildModel> ChildRp {
		get;
	} = new ();

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
}
