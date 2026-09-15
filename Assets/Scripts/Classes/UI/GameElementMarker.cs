using System.Linq;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Элемент интерфейса, нужный лишь игре с его командами либо компонентами: корень окна, кнопка, слот, полоска.
	/// Клиент один на все игры, а состав команд и компонентов у каждой свой: элемент, которому в этой игре
	/// нечего слать и нечего показывать, гасится при загрузке сцены. Живёт, пока у игры есть хоть одна его
	/// команда (перечень из пакета входа) либо хоть один его компонент (справочник компонентов игры).
	///
	/// Решение принимается один раз, в Awake: код, который позже сам включает объект, его перекрывает, —
	/// маркер ставится на объект, чью активность код не ведёт. Оба источника к Awake готовы: их кладёт вход
	/// в игру до загрузки игровой сцены (SigninController.LoadMain).
	///
	/// Объект, чью активность ведёт код, а функционал тот же, что у элемента с маркером, своего маркера не
	/// несёт: код сам спрашивает <see cref="IsNeeded"/> у маркера того элемента и без его согласия объект не
	/// включает. Перечень команд и компонентов так остаётся одним на оба. Якорь: ActionBarsController.ShowMobileActions.
	/// </summary>
	[DisallowMultipleComponent]
	public class GameElementMarker : MonoBehaviour
	{
		[Tooltip("Команды, которые элемент шлёт: полный адрес «группа/действие», действие указывается всегда, в том числе index")]
		[SerializeField]
		private string[] commands = new string[0];

		[Tooltip("Компоненты игры, которые элемент показывает: имя компонента")]
		[SerializeField]
		private string[] components = new string[0];

		private void Awake()
		{
			if (!IsNeeded())
				gameObject.SetActive(false);
		}

		/// <summary>
		/// Нужен ли элемент этой игре: есть ли у неё хоть одна его команда либо хоть один его компонент.
		/// Негодно заданный маркер уводит игрока на экран входа (ConnectController.Error) и отвечает false.
		/// </summary>
		public bool IsNeeded()
		{
			if (commands.Length == 0 && components.Length == 0)
			{
				ConnectController.Error("Маркеру элементов игры на объекте " + name + " не задано ни команд, ни компонентов");
				return false;
			}

			// Все записи проверяются на форму до решения: опечатка в любой из них иначе пряталась бы за
			// найденной соседней.
			bool needed = false;

			foreach (string command in commands)
			{
				// Группа сама бывает вложенной и содержит «/», поэтому действие — последний сегмент адреса.
				int slash = command.LastIndexOf('/');
				if (slash <= 0 || slash == command.Length - 1)
				{
					ConnectController.Error("Маркер элементов игры на объекте " + name + ": команда «" + command + "» задана не полным адресом «группа/действие»");
					return false;
				}

				needed |= ConnectController.HasPublicEvent(command.Substring(0, slash), command.Substring(slash + 1));
			}

			foreach (string component in components)
			{
				if (string.IsNullOrEmpty(component))
				{
					ConnectController.Error("Маркер элементов игры на объекте " + name + ": пустое имя компонента");
					return false;
				}

				needed |= ComponentCacheService.GetSlugs().Contains(component);
			}

			return needed;
		}
	}
}
