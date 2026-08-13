using System;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Окно «сколько штук»: игрок отдаёт стак, а отдать хочет часть — продать торговцу либо выбросить
	/// на землю. Без него уходит весь стак разом, и двадцать шесть яблок продаются одним промахом.
	///
	/// Окно про ВВОД, а не про сделку: потолок ему называет спрашивающий (он один знает, чем ограничен —
	/// размером стака, кассой торговца), ответ уходит обратным вызовом. Стак из одной единицы вопроса не
	/// стоит: спрашивающий такой случай решает сам, не поднимая окна.
	///
	/// Окно одно на все случаи, поэтому ссылки статические (паттерн LootWindowController): вызов идёт из
	/// Item.Use, где инстанса контроллера под рукой нет.
	/// </summary>
	abstract public class QuantityPromptController : CursorController
	{
		[Header("Для окна выбора количества")]

		// панель окна (включается на время вопроса)
		[SerializeField]
		private CanvasGroup quantityGroup;

		// подпись: что отдаём и каков потолок — иначе про лимит игрок узнаёт, лишь упёршись в него
		[SerializeField]
		private Text quantityTitle;

		[SerializeField]
		private InputField quantityInput;

		[SerializeField]
		private Button quantityOkButton;

		[SerializeField]
		private Button quantityCancelButton;

		// Во что обойдётся выбранное количество. Пусто у случаев без платы (выброс стака в мир): нулевая
		// сумма там значила бы «даром», хотя платы нет вовсе.
		[SerializeField]
		private Text quantityPrice;

		private static CanvasGroup _group;
		private static Text _title;
		private static InputField _input;
		private static Text _price;
		private static Button _ok;

		// потолок ввода и адресат ответа: окно одно, а спрашивающие разные
		private static int _max;
		private static Action<int> _onConfirm;

		// Во сколько обойдётся N единиц — считает спрашивающий (у него ценник и сторона сделки), окно лишь
		// показывает. null — платы нет: окно про количество, о деньгах оно само ничего не знает.
		private static Func<int, string> _priceHint;

		protected override void Awake()
		{
			base.Awake();

			// статики чистить вручную: Enter Play Mode без Domain Reload их не сбрасывает
			_onConfirm = null;
			_priceHint = null;
			_max = 0;

			if (quantityGroup == null || quantityTitle == null || quantityInput == null)
			{
				Error("не назначено окно выбора количества (панель, заголовок, поле ввода)");
				return;
			}

			if (quantityOkButton == null || quantityCancelButton == null)
			{
				Error("не назначены кнопки окна выбора количества");
				return;
			}

			if (quantityPrice == null)
			{
				Error("не назначена строка стоимости в окне выбора количества");
				return;
			}

			_group = quantityGroup;
			_title = quantityTitle;
			_input = quantityInput;
			_price = quantityPrice;
			_ok = quantityOkButton;

			// Метка модального окна: общее закрытие меню (CloseAllMenu) идёт тем же кликом, которым
			// игрок отдал предмет, — без метки заданный вопрос гас бы в том же кадре, и выброс части
			// стака в мир не доходил бы до отправки вовсе. Ставится кодом, как и метки окна добычи.
			if (quantityGroup.GetComponent<ModalWindowMarker>() == null)
				quantityGroup.gameObject.AddComponent<ModalWindowMarker>();

			quantityOkButton.onClick.RemoveListener(Confirm);
			quantityOkButton.onClick.AddListener(Confirm);
			quantityCancelButton.onClick.RemoveListener(Cancel);
			quantityCancelButton.onClick.AddListener(Cancel);

			quantityInput.onValueChanged.RemoveListener(OnInput);
			quantityInput.onValueChanged.AddListener(OnInput);

			// Enter — привычный ответ на вопрос с одним полем: без него игрок жмёт его же, ничего не
			// происходит, а модальное окно держит интерфейс — и это читается зависанием всей игры.
			SubmitRelay relay = quantityInput.GetComponent<SubmitRelay>();
			if (relay == null)
				relay = quantityInput.gameObject.AddComponent<SubmitRelay>();
			relay.Submitted = OnSubmit;

			Hide();
		}

		/// <summary>Окно сейчас спрашивает — второй вопрос поверх первого не задаём.</summary>
		public static bool IsOpen
		{
			get { return _group != null && _group.alpha > 0; }
		}

		/// <summary>
		/// Спросить количество и вызвать onConfirm с ответом. max — потолок: сколько отдать физически
		/// возможно (размер стака, а у продажи ещё и касса торговца). Потолок не рекомендация: больше него
		/// ввести не дают — сервер считает превышение нарушением договорённости и снимает игрока с карты,
		/// поэтому заведомо негодное число до него не доходит.
		/// priceHint — во что обойдётся N единиц (строкой, готовой к показу); null — платы нет вовсе.
		/// Окно уже открыто — новый вопрос отбрасываем: перебить чужой вопрос значит ответить не на тот.
		/// </summary>
		public static void Ask(string title, int max, Action<int> onConfirm, Func<int, string> priceHint = null)
		{
			if (_group == null || _input == null || onConfirm == null || max <= 0) return;
			if (IsOpen) return;

			_max = max;
			_onConfirm = onConfirm;
			_priceHint = priceHint;

			if (_title != null)
				_title.text = title + " (не больше " + max + ")";

			// Больше цифр, чем в самом потолке, не набрать — лишние символы поле просто не примет.
			_input.characterLimit = max.ToString().Length;
			_input.text = max.ToString();
			OnInput(_input.text);

			_group.alpha = 1;
			_group.blocksRaycasts = true;
			_group.interactable = true;

			_input.Select();
			_input.ActivateInputField();
		}

		/// <summary>
		/// Ввод изменился: держим его в границах и пересчитываем стоимость. Число выше потолка сразу
		/// заменяем потолком — так игрок видит предел, а не узнаёт о нём отказом после подтверждения.
		/// Пустое поле и негодное число (ноль, минус) оставляем как есть — стирать за игроком набранное
		/// нельзя, — но подтверждение на них не даём: уйдёт неизвестно что.
		/// </summary>
		private static void OnInput(string raw)
		{
			int count;
			bool parsed = int.TryParse(raw, out count);

			if (parsed && count > _max)
			{
				_input.text = _max.ToString();      // повторный вызов придёт уже с потолком
				return;
			}

			bool valid = parsed && count >= 1;

			if (_ok != null)
				_ok.interactable = valid;

			if (_price != null)
				_price.text = valid && _priceHint != null ? _priceHint(count) : "";
		}

		/// <summary>
		/// Ответ клавишей Enter (см. SubmitRelay). Негодное число окно не закрывает: вопрос остаётся
		/// заданным, иначе Enter по пустому полю отменял бы сделку молча.
		/// </summary>
		private static void OnSubmit()
		{
			if (!IsOpen || (_ok != null && !_ok.interactable))
				return;

			Confirm();
		}

		/// <summary>
		/// Подтверждение: пустой либо неразборчивый ввод считаем за отказ от торга — молча слать вместо
		/// него весь стак нельзя, это ровно та потеря, ради которой окно и заведено.
		/// Границы держит сам ввод (OnInput), здесь они проверяются ещё раз: за пределами потолка сервер
		/// считает команду нарушением договорённости и снимает игрока с карты, поэтому последний рубеж
		/// перед отправкой стоит там же, где и отправка.
		/// </summary>
		private static void Confirm()
		{
			Action<int> callback = _onConfirm;
			int max = _max;
			string raw = _input != null ? _input.text : null;

			Hide();

			int count;
			if (callback == null || !int.TryParse(raw, out count)) return;
			if (count < 1 || count > max) return;

			callback(count);
		}

		private static void Cancel()
		{
			Hide();
		}

		private static void Hide()
		{
			_onConfirm = null;
			_priceHint = null;
			_max = 0;

			if (_price != null)
				_price.text = "";

			// Кнопку возвращаем в рабочее состояние здесь: следующий вопрос откроется с годным числом,
			// а погашенной она осталась бы от прошлого ввода.
			if (_ok != null)
				_ok.interactable = true;

			if (_group == null) return;

			_group.alpha = 0;
			_group.blocksRaycasts = false;
		}
	}
}
