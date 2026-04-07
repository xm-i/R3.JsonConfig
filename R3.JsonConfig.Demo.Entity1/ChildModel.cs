using R3.JsonConfig.Attributes;

namespace R3.JsonConfig.Demo.Entity1;

/// <summary>
/// 子モデル。
/// </summary>
[GenerateR3JsonConfigDto]
public class ChildModel {
	/// <summary>
	/// 子モデルの名前。
	/// </summary>
	public string? Name {
		get;
		set;
	}
}