using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	public class PlayerModel : EnemyModel
	{
		/// <summary>
		/// Адрес подключения игрока: сервер берёт его из самого соединения и шлёт всем, кто видит игрока.
		/// Показывает окно сведений о цели.
		/// </summary>
		[NonSerialized]
		public string ip;

		/// <summary>
		/// Действие команды подбора — имя серверное. Группу той же команды держит структура её отправки
		/// (<see cref="PickupResponse.GROUP"/>): группа одна и та же, куда бы команда ни шла.
		/// </summary>
		private const string PickupAction = "index";

		/// <summary>
		/// Сколько секунд поручение подобрать вещь считается свежим. Без срока вещь, пропавшая позже и по другой
		/// причине (её взял другой, истёк срок лежания), улетала бы к тому, кто до неё так и не дошёл.
		/// </summary>
		private const double ClaimLifetime = 3;

		/// <summary>
		/// Вещи, которые сервер поручил взять этому игроку, и до какого момента поручение свежо. Команда подбора
		/// приходит раньше, чем вещь снимают с карты, а в тот самый миг она уже завершена и данных не несёт —
		/// поэтому ключи запоминаются здесь, а не читаются из команды в момент показа.
		/// </summary>
		private readonly Dictionary<string, DateTime> pickupClaim = new Dictionary<string, DateTime>();

		/// <summary>
		/// Поручал ли сервер этому игроку взять названную вещь и не устарело ли поручение.
		/// </summary>
		public bool ClaimedPickup(string entity)
		{
			return pickupClaim.TryGetValue(entity, out DateTime until) && until > DateTime.Now;
		}

		/// <summary>
		/// Запоминает ключи вещей из пришедшей команды подбора. Заодно снимает просроченные: поручение по вещи,
		/// которую игрок не взял, иначе оставалось бы в памяти до его ухода с карты.
		/// </summary>
		private void RememberPickup()
		{
			Event pickup = TryGetEvent(PickupResponse.GROUP);

			if (pickup == null || pickup.action != PickupAction || pickup.data == null)
				return;

			JToken target = pickup.data["target"];

			if (target == null || target.Type == JTokenType.Null)
				return;

			DateTime now = DateTime.Now;

			foreach (string stale in new List<string>(pickupClaim.Keys))
				if (pickupClaim[stale] <= now)
					pickupClaim.Remove(stale);

			DateTime until = now.AddSeconds(ClaimLifetime);

			if (target.Type == JTokenType.Array)
				foreach (JToken entity in target)
					pickupClaim[(string)entity] = until;
			else
				pickupClaim[(string)target] = until;
		}

        public override void SetData(EntityRecive recive)
		{
			PrepareComponents(((PlayerRecive)recive).components);
			this.SetData((PlayerRecive)recive);
		}

		private void SetData(PlayerRecive recive)
		{
			if (recive.ip != null)
				this.ip = recive.ip;

			base.SetData(recive);

			RememberPickup();
		}

		// Был ли в прошлом кадре «призрачный» режим — для однократного возврата непрозрачности на выходе.
		private bool _ghost;

		// «Призрачный» режим: hp=0 и action != dead (труп лежит при action=dead с полной альфой,
		// а двигается призраком — полупрозрачно). Условие пересчитывается каждый кадр в LateUpdate,
		// поэтому переключения action ↔ dead/walk/idle подхватываются автоматически без отдельных
		// триггеров от SetData. LateUpdate идёт после всех Update в кадре: тело пересобирает свою позу
		// в Update, и однократно выставленная прозрачность затиралась бы ею на том же кадре.
		// Живёт в PlayerModel (а не в EnemyModel): призраком-в-движении становится только игрок
		// (corpse-run при hp=0); enemy/animal просто умирают. Поля hp/action унаследованы.
		void LateUpdate()
		{
			bool ghost = hp == 0 && action != "dead";
			if (ghost)
				SetBodyAlpha(0.5f);
			// При выходе из призрака (воскрешение / переход в action=dead) явно возвращаем непрозрачность.
			// Полагаться на то, что тело само перезапишет прозрачность обратно в 1, нельзя: fallback root-SR,
			// неанимированные сущности и скрытые в текущем кадре части тела оно не трогает — без этого
			// возврата они застревают на alpha=0.5 (игрок «остаётся прозрачным» после получения HP).
			else if (_ghost)
				SetBodyAlpha(1f);
			_ghost = ghost;
		}
	}
}
