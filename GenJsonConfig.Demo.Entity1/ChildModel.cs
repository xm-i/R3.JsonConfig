using GenJsonConfig.Attributes;

namespace GenJsonConfig.Demo.Entity1;

/// <summary>
/// 子モデル。
/// </summary>
[GenerateJsonConfigDto]
public class ChildModel {
	/// <summary>
	/// 子モデルの名前。
	/// </summary>
	public string? Name {
		get;
		set;
	}
}