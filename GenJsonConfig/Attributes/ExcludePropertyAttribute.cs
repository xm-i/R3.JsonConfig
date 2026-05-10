namespace GenJsonConfig.Attributes;

/// <summary>
/// 特定のプロパティを DTO 変換の対象から明示的に除外する場合に使用します。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class ExcludePropertyAttribute : Attribute;