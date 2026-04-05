using System.Runtime.CompilerServices;

namespace R3.JsonConfig;

/// <summary>
/// CreateJson (Model -> DTO) 時に、循環参照を検出し $id/$ref を管理するためのトラッカー。
/// </summary>
public sealed class ReferenceTracker {
	private readonly Dictionary<object, string> _objectToId = new Dictionary<object, string>(new ReferenceEqualityComparer());
	private int _nextId = 1;

	/// <summary>
	/// インスタンスが既に登録されているか確認し、登録されている場合はその ID を返します。
	/// 未登録の場合は新しい ID を発行して登録し、null を返します。
	/// </summary>
	public string? GetOrAddId(object? model) {
		if (model is null) return null;

		if (_objectToId.TryGetValue(model, out var id)) {
			return id;
		}

		id = (_nextId++).ToString();
		_objectToId.Add(model, id);
		return null;
	}

	/// <summary>
	/// 登録済みのインスタンスの ID を取得します。
	/// </summary>
	public string GetId(object model) {
		return _objectToId[model];
	}

	private sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
		bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);
		int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
	}
}
