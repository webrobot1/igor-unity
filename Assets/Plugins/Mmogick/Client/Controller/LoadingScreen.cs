using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Панель загрузки — закрывает экран на паузах, когда показывать игроку нечего: вход в игру, ожидание
	/// подъёма карты, переход на карту, адреса которой сервер не дал.
	///
	/// Живёт вне сцен (DontDestroyOnLoad): пауза охватывает и сцену входа, и игровую, а они выгружают друг
	/// друга — панель, лежащая на любой из них, исчезла бы посреди паузы. Отдельного поля «показана» нет:
	/// состояние несёт активность самой панели.
	///
	/// Переключают её из ГЛАВНОГО потока (клик кнопки входа, разбор мира, опрос в MapController.Update):
	/// SetActive — Unity API, из потока приёма пакетов его звать нельзя.
	/// </summary>
	public class LoadingScreen : MonoBehaviour
	{
		/// <summary>
		/// Ступени ожидания в порядке прохождения. Полоса закрашена на «номер ступени плюс доля внутри неё»,
		/// делённое на число ступеней, потому порядок объявления — это и есть порядок на полосе.
		///
		/// Долю внутри ступени сообщает тот, кто её проходит, и только там, где её есть чем мерить: скачивание
		/// отдаёт долю принятых байт, загрузка сцены — свою готовность. Ожидание сервера не измеримо ничем —
		/// такая ступень просто занимает свой отрезок целиком, когда начинается.
		/// </summary>
		public enum Stage
		{
			Auth,        // запрос авторизации, включая ожидание подъёма карты сервером
			Tiles,       // кеш графики карт
			Components,  // справочник компонентов игры
			Animations,  // кеш анимаций
			Scene,       // игровая сцена
			World,       // мир от сервера
			Map,         // графика карты вокруг игрока
			Ready        // персонаж показан — полоса закрашена целиком
		}

		/// <summary>
		/// Сама панель. Корень держим активным всегда: на выключенном объекте не отработает ни Awake,
		/// ни регистрация единственного экземпляра.
		/// </summary>
		[SerializeField]
		private GameObject panel;

		/// <summary>
		/// Закрашенная часть полосы. Растягивается по ширине рамки: якорь справа двигается, слева стоит.
		/// </summary>
		[SerializeField]
		private RectTransform progressFill;

		[SerializeField]
		private Text progressText;

		private static LoadingScreen instance;

		private void Awake()
		{
			// Сцена входа загружается заново при каждом возврате на неё, а прежний экземпляр пережил её
			// выгрузку — второй лишний.
			if (instance != null && instance != this)
			{
				Destroy(gameObject);
				return;
			}

			if (panel == null || progressFill == null || progressText == null)
				throw new System.Exception("Панель загрузки: не присвоены объект панели, полоса либо подпись");

			instance = this;
			DontDestroyOnLoad(gameObject);
		}

		public static void Show()
		{
			if (instance == null)
				throw new System.Exception("Панель загрузки: экземпляра нет — вход в игру начинается со сцены входа, она его и несёт");

			if (!instance.panel.activeSelf)
			{
				instance.panel.SetActive(true);
				instance.Fill(0f);
			}
		}

		public static void Hide()
		{
			// Экземпляра нет только до первого Awake сцены входа — скрывать в этот момент нечего.
			if (instance != null && instance.panel.activeSelf)
				instance.panel.SetActive(false);
		}

		/// <summary>
		/// Поднята ли панель. Спрашивает тот, кто идёт по ступеням в игровой сцене: его код крутится и при
		/// переходе между соседними картами открытого мира, где панель не поднимают вовсе, — сообщённая
		/// ступень подняла бы её (см. SetStage).
		/// </summary>
		public static bool IsShown => instance != null && instance.panel.activeSelf;

		/// <summary>
		/// Ступень пройденного ожидания; within — доля внутри неё (0..1) там, где её есть чем мерить.
		/// Панель поднимает сама: ступень сообщают только по ходу паузы, а на первой из них панель могла
		/// ещё не подняться (вход заново после перехода начинается с авторизации, а не с кнопки).
		/// </summary>
		public static void SetStage(Stage stage, float within = 0f)
		{
			Show();

			// Ступени считаем от единицы: начатая ступень — уже пройденная часть пути, и первая из них не
			// оставляет игрока перед нулём на всё ожидание сервера. Знаменатель на ту же единицу больше,
			// поэтому последняя ступень по-прежнему закрашивает полосу целиком.
			instance.Fill(((int)stage + Mathf.Clamp01(within) + 1f) / ((int)Stage.Ready + 1f));
		}

		private void Fill(float value)
		{
			value = Mathf.Clamp01(value);

			// Полоса закрашивается СЛЕВА направо: левый край якоря стоит, правый едет за долей.
			progressFill.anchorMin = new Vector2(0f, 0f);
			progressFill.anchorMax = new Vector2(value, 1f);
			progressFill.offsetMin = Vector2.zero;
			progressFill.offsetMax = Vector2.zero;

			progressText.text = "Загрузка… " + Mathf.RoundToInt(value * 100f) + "%";
		}
	}
}
