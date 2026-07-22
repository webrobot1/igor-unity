namespace Mmogick
{
	/// <summary>
	/// Разделяемое состояние видимости отладочных слоёв карты и их имена. Лежит в firstpass-сборке
	/// (Assets/Plugins), потому что MapDecodeModel (firstpass) читает эти флаги при создании слоёв карты,
	/// а переключает их UI-панель DebugPanelController (Assembly-CSharp), чьи типы firstpass не видит.
	/// Источник истины видимости — галочки debug-панели; эти static-поля они и выставляют.
	/// </summary>
	public static class DebugLayers
	{
		// Имена per-map слоёв — единый источник: MapDecodeModel создаёт объекты с этими именами,
		// DebugPanelController по ним ищет слои в иерархии карты.
		public const string COLLISION = "DebugCollision";
		public const string GRID = "DebugGrid";
		public const string OBJECTS = "DebugObjects";

		// Текущее состояние галочек. MapDecodeModel.generate читает при создании слоёв карты (карты грузятся
		// асинхронно, позже входа), DebugPanelController выставляет по галочкам и применяет к загруженным картам.
		public static bool ShowCollision;
		public static bool ShowGrid;
		public static bool ShowObjects;
	}
}
