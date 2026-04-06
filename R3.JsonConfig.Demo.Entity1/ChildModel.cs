using R3.JsonConfig.Attributes;

namespace R3.JsonConfig.Demo.Entity1;

[GenerateR3JsonConfigDto]
public class ChildModel {
	public string Name {
		get;
		set;
	} = "ChildName";
}