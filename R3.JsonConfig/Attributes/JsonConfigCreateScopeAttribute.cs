namespace R3.JsonConfig.Attributes;

/// <summary>
/// プロパティ単位で、モデルの生成時に新しい DI スコープを作成するかどうかを制御します。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class JsonConfigCreateScopeAttribute : Attribute;