using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
	/// <summary>
	/// Мигающая надпись поверх игры о перезагрузке карты. Для игрока перезагрузка выглядит зависанием:
	/// сервер на несколько секунд перестаёт читать соединение, персонаж не идёт, команды отбиты паузой.
	/// Без надписи это неотличимо от обрыва связи — игрок продолжает жать движение и считает игру сломанной.
	///
	/// Своего состояния не держит: спрашивает соединение (<see cref="ConnectController.IsReloading"/>).
	/// Второй носитель того же состояния разошёлся бы с первым молча.
	///
	/// Мигаем включением самой надписи, а не прозрачностью букв: контур рисует отдельный компонент и
	/// прозрачность букв за собой не повторяет — на полпути буквы бледнели бы, а обводка стояла в полную
	/// силу. Потому компонент висит НЕ на самой надписи: у выключенного объекта Update не идёт.
	/// </summary>
	public class ReloadNotice : MonoBehaviour
	{
		/// <summary>Полный цикл мигания, секунды: показана плюс скрыта.</summary>
		private const float BLINK_SEC = 1f;

		/// <summary>Доля цикла, которую надпись видна.</summary>
		private const float BLINK_VISIBLE = 0.7f;

		[SerializeField]
		private Text notice;

		private void Awake()
		{
			if (notice == null)
			{
				// Компонент снимаем сами: Error уводит игрока на экран входа следующим кадром цикла связи,
				// но исполнение продолжается, и Update успел бы обратиться к неприсвоенной надписи.
				enabled = false;
				ConnectController.Error("не присвоен Text надписи о перезагрузке карты");
				return;
			}

			notice.gameObject.SetActive(false);
		}

		private void Update()
		{
			// Время без учёта игровой паузы: надпись обязана мигать и тогда, когда мир стоит.
			bool show = ConnectController.IsReloading
				&& Mathf.Repeat(Time.unscaledTime, BLINK_SEC) < BLINK_SEC * BLINK_VISIBLE;

			if (notice.gameObject.activeSelf != show)
				notice.gameObject.SetActive(show);
		}
	}
}
