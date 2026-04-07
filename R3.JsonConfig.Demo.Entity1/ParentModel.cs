using System.Drawing;

using ObservableCollections;

using R3.JsonConfig.Attributes;
using R3.JsonConfig.Demo.Composition.Store;

namespace R3.JsonConfig.Demo.Entity1;

[GenerateR3JsonConfigDto]
public class ParentModel {

	/// <summary>
	/// 文字列型の ReactiveProperty。
	/// </summary>
	public ReactiveProperty<string> StringRp {
		get;
	} = new();

	/// <summary>
	/// Nullable な Color 型の ReactiveProperty。
	/// </summary>
	public ReactiveProperty<Color?> ColorRp {
		get;
	} = new();

	/// <summary>
	/// ChildModel 型の ReactiveProperty。
	/// </summary>
	public ReactiveProperty<ChildModel> ChildRp {
		get;
	} = new();

	/// <summary>
	/// int 型の配列を管理する ObservableList。
	/// </summary>
	public ObservableList<int> IntArray {
		get;
	} = [];

	/// <summary>
	/// Nullable な Color 型の配列を管理する ObservableList。
	/// </summary>
	public ObservableList<Color?> ColorArray {
		get;
	} = [];

	/// <summary>
	/// ChildModel 型の配列を管理する ObservableList。
	/// </summary>
	public ObservableList<ChildModel> ChildArray {
		get;
	} = [];

	/// <summary>
	/// 通常の文字列プロパティ。
	/// </summary>
	public string? StringProperty {
		get;
		set;
	}

	/// <summary>
	/// 通常の Nullable な Color プロパティ。
	/// </summary>
	public Color? ColorProperty {
		get;
		set;
	}

	/// <summary>
	/// 通常の ChildModel プロパティ。
	/// </summary>
	public ChildModel? ChildProperty {
		get;
		set;
	}

	/// <summary>
	/// ポリモーフィックなプラグイン設定（インターフェース）。
	/// </summary>
	public IPluginConfig? Plugin {
		get;
		set;
	}

	/// <summary>
	/// 2つ目のポリモーフィックなプラグイン設定。
	/// </summary>
	public IPluginConfig? Plugin2 {
		get;
		set;
	}

	/// <summary>
	/// IPluginConfig 側の ReactiveProperty。
	/// </summary>
	public ReactiveProperty<IPluginConfig> PluginRp {
		get;
	} = new();

	/// <summary>
	/// IPluginConfig を格納する ObservableList。
	/// </summary>
	public ObservableList<IPluginConfig> PluginList {
		get;
	} = [];
}