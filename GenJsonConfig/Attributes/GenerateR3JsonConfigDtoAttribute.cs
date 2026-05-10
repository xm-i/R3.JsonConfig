namespace GenJsonConfig.Attributes;

/// <summary>
/// この属性が付与されたクラスまたはインターフェースに対して、JSON シリアライズ用の DTO と変換メソッドを自動生成します。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
public class GenerateJsonConfigDtoAttribute : Attribute;