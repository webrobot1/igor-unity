namespace Mmogick
{
	/// <summary>
	/// Структура полученных данных при авторизации
	/// </summary>
	[System.Serializable]
	public class SigninRecive
	{
		public string host;

		public string key;
		public string token;

		public float step;
		public int fps;

		public int map;
		public int game;

		public int position_precision;

		// Имя action, обозначающего «idle» (сущность в покое). Клиент использует его для:
		//   1) Normalize-sampling в SpriterPostImportAdjuster Phase 1 (стабильная поза для median-bounds).
		//   2) Таймаут-перехода в idle у legacy Animator (ObjectModel.Update).
		// Сервер шлёт это поле под именем "idle" (не "idle_action") — замапливаем через JsonProperty,
		// чтобы локальное имя оставалось семантически понятным (не "idle", которое сбивало бы:
		// "флаг покоя?" vs "имя action-а"). По контракту поле шлётся ВСЕГДА и имеет непустое значение —
		// default'а здесь намеренно нет, пропущенное/пустое поле = нарушение контракта (см. CLAUDE.md:
		// «Не возвращать молча null/default… нарушение контракта должно падать громко»). Проверка — в SigninController.
		[Newtonsoft.Json.JsonProperty("idle")]
		public string idle_action;

		// Game-level справочник всех slot-slug-ов экипировки разрешённых в этой игре (head/chest/hand_r/...).
		// Подмножество User.equipmentSlot, выбранное в админке. Значение Dictionary всегда true — формат
		// (slug → true) выбран чтобы Newtonsoft не путал с массивом при сериализации.
		// Применение: рисование ячеек инвентаря; валидация что prefab.equipable_slot (из /prefabs)
		// и AEOS.slot (из /animations/{id}.object_slot) лежат в этом наборе.
		public System.Collections.Generic.Dictionary<string, bool> equipment_slot;

		/// <summary>
		/// возможные ошибки (если не пусто - произойдет разъединение, но где быстрее - в клиенте или на сервере сказать сложно)
		/// </summary>
		public string error = "";
	}
}
