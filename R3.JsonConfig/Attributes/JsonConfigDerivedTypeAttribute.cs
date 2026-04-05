using System;

namespace R3.JsonConfig.Attributes;

/// <summary>
/// ポリモーフィックなシリアライズにおいて、基底クラスまたはインターフェースから派生した型を登録するために使用します。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true)]
public class JsonConfigDerivedTypeAttribute : Attribute {
	/// <summary>派生型。</summary>
	public Type DerivedType {
		get;
	}
	/// <summary>型識別子（Discriminator）。JSON 内の ___Type プロパティに使用されます。</summary>
	public string TypeDiscriminator {
		get;
	}

	/// <summary>
	/// <see cref="JsonConfigDerivedTypeAttribute"/> クラスの新しいインスタンスを初期化します。
	/// </summary>
	/// <param name="derivedType">登録する派生型。</param>
	/// <param name="typeDiscriminator">JSON 内で使用する型識別文字列。</param>
	public JsonConfigDerivedTypeAttribute(Type derivedType, string typeDiscriminator) {
		this.DerivedType = derivedType;
		this.TypeDiscriminator = typeDiscriminator;
	}
}