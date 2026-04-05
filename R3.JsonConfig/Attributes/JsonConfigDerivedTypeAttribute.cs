using System;

namespace R3.JsonConfig.Attributes;

/// <summary>
/// ポリモーフィックなシリアライズにおいて、具象クラスが基底型の派生であることを宣言するために使用します。
/// 基底型（インターフェースまたは抽象クラス）ではなく、派生側のクラスに付与してください。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class JsonConfigDerivedTypeAttribute : Attribute {
	/// <summary>型識別子（Discriminator）。JSON 内の ___Type プロパティに使用されます。</summary>
	public string TypeDiscriminator {
		get;
	}

	/// <summary>
	/// <see cref="JsonConfigDerivedTypeAttribute"/> クラスの新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="typeDiscriminator">JSON 内で使用する型識別文字列。</param>
	public JsonConfigDerivedTypeAttribute(string typeDiscriminator) {
		this.TypeDiscriminator = typeDiscriminator;
	}
}