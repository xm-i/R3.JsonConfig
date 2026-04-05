namespace R3.JsonConfig;

/// <summary>
/// CreateModel (DTO -> Model) 時に、$id/$ref に基づいてインスタンスを解決するためのリゾルバー。
/// </summary>
public sealed class ReferenceResolver {
	private readonly Dictionary<string, object> _idToObject = new Dictionary<string, object>();

	/// <summary>
	/// 指定した ID でインスタンスを登録します。
	/// </summary>
	public void Add(string id, object model) {
		this._idToObject[id] = model;
	}

	/// <summary>
	/// 指定した ID に対応するインスタンスを解決します。
	/// </summary>
	public T? Resolve<T>(string id) where T : class {
		if (this._idToObject.TryGetValue(id, out var model)) {
			return (T)model;
		}
		return null;
	}
}