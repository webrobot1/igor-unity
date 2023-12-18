using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyFantasy
{
	/// <summary>
	/// колайдер обязателен тк мы кликаем на gameObject что бы выделить его  в область колайдера. этот клас наследуется от плагина и реулизует работу с анимацией. вы можете реализовать по своему (поэтому работа с ней не часть плагина)
	/// </summary>
	[RequireComponent(typeof(Collider))]
	public class NewObjectModel : ObjectModel
	{

		[NonSerialized]
		public Animator animator;

		/// <summary>
		/// список анимационных тригеров
		/// </summary>
		private static Dictionary<string, bool> trigers;

		/// <summary>
		///  активный слой анимации
		/// </summary>
		[NonSerialized]
		public int layerIndex = 0;

		/// <summary>
		/// может быть null если мы через этот класс выделилил объект оно именно тут для совместимости как и то что ниже
		/// </summary>
		[NonSerialized]
		public int? hp = null;

		/// <summary>
		/// поле с жизнями выделленого существа
		/// </summary>
		[NonSerialized]
		public Image lifeBar;

		/// <summary>
		/// может быть null если мы через этот класс выделилил объект
		/// </summary>
		[NonSerialized]
		public int? mp = null;

		[NonSerialized]
		public int hpMax;

		[NonSerialized]
		public int mpMax;

		private Dictionary<string, Coroutine> coroutines = new Dictionary<string, Coroutine>();

		/// <summary>
		///  это сторона движения игркоа. как transform forward ,  автоматом нормализует значения
		/// </summary>
		public override Vector3 forward
		{
			get { return base.forward; }
			set 
			{
				// вообще сервер сам нормализует но так уменьшиться пакет размера символов
				base.forward = value;

				//это Blend tree аниматора (в игре Игорья решил так вопрос с анимацией движения в разных направлениях. рекомендую и Вам)
				if (animator)
				{
					if (animator.GetFloat("x") != value.x)
						animator.SetFloat("x", value.x);
					if (animator.GetFloat("y") != value.y)
						animator.SetFloat("y", value.y);
				}
			}
		}

		protected virtual void Awake()
		{
			if (animator = GetComponent<Animator>())
			{
				// сохраним все возможные Тригеры анимаций и, если нам пришел action как тигер - обновим анимацию
				if (trigers == null)
				{
					trigers = new Dictionary<string, bool>();
					foreach (var parameter in animator.parameters.Where(parameter => parameter.type == AnimatorControllerParameterType.Trigger))
					{
						trigers.Add(parameter.name, true);
					}
				}
			}
		}

		// Update is called once per frame
		void Update()
		{
			// если текущий наш статус анимации - не стояние и давно небыло активности - включим анмацию остановки
			if (
				animator != null 
					&& 
				action != "dead"
					&&
				action != ConnectController.ACTION_REMOVE
					&&
				DateTime.Compare(activeLast.AddMilliseconds(300), DateTime.Now) < 1
					&&
				(animator.GetCurrentAnimatorStateInfo(layerIndex).loop || animator.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime >= 1.0f) 	
			)
			{
				string layer_name = animator.GetLayerName(layerIndex);
                if (layer_name != "idle")
                {
					Log("Анимация " + key + " с " + action + " на idle (таймаут)");
					Animate(animator, animator.GetLayerIndex("idle"));
				}
			}
		}

		/// <summary>
		/// этот метод для возможноости переопределения его же самого нужен но с другими типами аргументов
		/// </summary>
		public override void SetData(ObjectRecive recive)
		{
			this.SetData((NewObjectRecive)recive);
		}

		/// <summary>
		/// переопределим метод срабатываемый при присвоениеии пришедших с сервера данных и начнем включать анимацию
		/// </summary>
		protected void SetData(NewObjectRecive recive)
		{
			// пришла команды удаления с карты объекта
			if (recive.action == ConnectController.ACTION_REMOVE && action != recive.action)
			{
				action = recive.action;
				StartCoroutine(this.Remove(recive.map_id != null));
			}

			Vector3 old_position = position;
			int old_map_id = map_id;

			base.SetData(recive);

			// при первой загрузке не запускаем
			if ((recive.x != null || recive.y != null || recive.z != null) && old_position != position)
			{
				Vector3 new_position = new Vector3(recive.x ?? old_position.x, recive.y ?? old_position.y, recive.z ?? old_position.z);

				// если первый вход в игру
				if (old_position == Vector3.zero) 
					transform.localPosition = new_position;
				else
				{            
					Log("Движение - новые данные с сервера о переходе с "+ old_position + " на "+ new_position+" существа в локальной позиции "+transform.localPosition);
					if (coroutines.ContainsKey("walk"))
					{
						LogWarning("Движение - существо еще не звершило движение. Эстраполяция: " + Math.Round((Vector3.Distance(transform.localPosition, old_position) / Vector3.Distance(old_position, new_position)) * 100) + " % не дойдя с прошлого движения");
					}

					if ((recive.action == "walk" && (old_position + (forward * ConnectController.step)).ToString() == new_position.ToString()) || (recive.map_id!=null && recive.map_id != old_map_id))
					{
						// до получения новых пакетов продолжим движение
						if (recive.map_id != null)
						{
							Log("Движение - Переход между локациями");
							recive.action = "walk";
							position = new_position = old_position + (forward * ConnectController.step);
						}

						// в приоритете getEvent(WalkResponse.GROUP).timeout  тк мы у него не отнимаем время пинга на получение пакета но и не прибавляем ping время на отправку с сервера нового пакета
						coroutines["walk"] = StartCoroutine(Walk(new_position, (coroutines.ContainsKey("walk") ? coroutines["walk"] : null)));
					}
					else
					{
						// выстрелы могут телепортироваться в конце что бы их взрыв был на клетке существа а негде то около рядом
						Log("Движение -телепорт из " + transform.localPosition + " в " + new_position);

						if (coroutines.ContainsKey("walk"))
						{
							Log("Движение - остановка корутины");
							StopCoroutine(coroutines["walk"]);
							coroutines.Remove("walk");
						}
						transform.localPosition = new_position;
					}
				}
			}

			// сгенерируем тригер - название анимации исходя из положения нашего персонажа и его действия
			// todo некоторые анимации не нужно запускать если существо только добавлено (например смерти тк умерло оно может уже давно а карта только загрузилась)
			if (recive.action != null)
			{
				if (animator != null && recive.action != ConnectController.ACTION_REMOVE)
				{
					int layerIndex = animator.GetLayerIndex(recive.action);
					if (layerIndex == -1)
					{
						LogWarning("Положение без группы-слоя анимации ");
					}
					else
					{
					//	LogWarning(DateTime.Now.Millisecond + " " + key + ": " + recive.action + " с " + action);
						Animate(animator, layerIndex);
					}
				}
			}
		}

		/// <summary>
		/// включить анимацию - те отключить все слои анимаций других и оставить только нужную. если есть анмиационный тригер одноименный со слоем - и его выключить (для анимаций которых не зацикленные и надо запустить один раз)
		/// </summary>
		public void Animate(Animator animator, int layerIndex)
		{
			if (layerIndex >=0)
			{
				if (layerIndex == 0 || animator.GetLayerWeight(layerIndex) != 1) 
				{ 
					// "остановим" все слои анмиации
					if (animator.layerCount > 1) 
					{ 
						for (int i = 1; i < animator.layerCount; i++)
						{
							animator.SetLayerWeight(i, 0);
						}
					}
					animator.SetLayerWeight(layerIndex, 1);		
				}
				this.layerIndex = layerIndex;

				string name = animator.GetLayerName(layerIndex);
				if (trigers.ContainsKey(name))
				{
					Log("запускаем тригер " + name);
					animator.SetTrigger(name);
				}
			}
			else
				PlayerController.Error("неверный индекс анимации "+ layerIndex);
		}

		/// <summary>
		/// при передижении игрока проигрывается анмиация передвижения по клетке (хотя для сервера мы уже на новой позиции). скорость равна времени паузы между командами на новое движение.
		/// она вошла в плагин тк движение нужно в любой игре а координаты часть стандартного функционала, вы можете переопределить ее
		/// корутина подымается не моментально так что остановим внутри нее старую что бы небыло дерганья между запускми и остановками
		/// </summary>
		/// <param name="position">куда движемя</param>
		protected virtual IEnumerator Walk(Vector3 finish, Coroutine old_coroutine)
		{
			if (old_coroutine != null)
			{
				Log("Движение - Остановка старой корутины с запуском новой");
				StopCoroutine(old_coroutine);
			}
			else
				Log("Движение - новая корутина корутины");

			if (finish == transform.localPosition)
			{
				LogError("Движение - позиция к которой движемся равна той на которой стоим");
				yield break;
			}

			float distance;

			// отрезок пути которой существо движется за кадр
			double timeout = getEvent(WalkResponse.GROUP).timeout ?? GetEventRemain(WalkResponse.GROUP);

			// добавляем 1 кадра тк пакет с новыми координатами может прийти во время во врмеся пауз между кадрами FixedUpdate
			timeout += Time.fixedDeltaTime;

			// фиксированная величина расчитывается и приходит от сервера
			if (ConnectController.extrapolation_time > 0)
			{
				timeout += ConnectController.extrapolation_time;
			}

			double last_ping_extropolation;
			if (ConnectController.EXTROPOLATION_PING > 0)
			{
				last_ping_extropolation = ConnectController.Ping();
				timeout += last_ping_extropolation * ConnectController.EXTROPOLATION_PING;
			}		

			// если мы уходим с карты надо замедлиться на время 2х полных пинга (1 - http запрос на авторизацию и его возврат, 2 - в websocket, получить от него пакет)
			// мы не првоеряем удаляется ли существо или именно переходит (в обоих случаях action одинаков, но при переходе новая карта указывается) тк при удалении окончательном эта корутина уничтожается с существом
			if (action == ConnectController.ACTION_REMOVE)
				timeout += ConnectController.Ping() * 2;

			// на сколько от шага каждый кадр сервера сдвигать существо
			double distancePerUpdate = (Vector3.Distance(transform.localPosition, finish) / (timeout / Time.fixedDeltaTime));
			bool extrapolation = false;
	
			bool isRemove = action == ConnectController.ACTION_REMOVE;
			while (true)
			{

				if (action != "walk" && action != ConnectController.ACTION_REMOVE)
				{
					LogWarning("Движение - Сменен action во время движения на " + action);
					transform.localPosition = finish;
					break;
				}

				distance = Vector3.Distance(transform.localPosition, finish);

				// если уже подошли но с сервера пришла инфа что следом будет это же событие группы - экстрополируем движение дальше
				if (distance < distancePerUpdate)
				{
                    /*if (getEvent(WalkResponse.GROUP).action.Length == 0)
					{
						LogWarning("Движение - с сервера пршел пакет что мы дальше не идем");
						transform.localPosition = finish;
						break;
					}*/

					// если отправлен пакет на движение, но еще нет возврата можем пройти еще чуть чуть
                    if((getEvent(WalkResponse.GROUP).isFinish == false && !extrapolation) || isRemove)
					{
						extrapolation = true;

						// добавим что идти нужно еще на пол шаг дальше в том же направлении
						Vector3 step = forward * ConnectController.step;
						finish += step;

						// замедлим время дополнительного нашага 
						if (ConnectController.EXTROPOLATION_PING > 0 && ConnectController.MaxPing() > last_ping_extropolation)
						{
							double slow = (last_ping_extropolation / ConnectController.MaxPing());
							distancePerUpdate = distancePerUpdate * slow;

							LogError("Движение - экстраполируем растоянием еще на " + step + ", уменьшим скорость движения учитывая новый ping (с "+ last_ping_extropolation*1000 + " на "+ ConnectController.MaxPing() * 1000 + ") на "+Math.Round((1-slow)*100)+" %");
						}
						else
							LogError("Движение - экстраполируем растоянием еще на " + step);
					}
                    else 
					{ 
						if(getEvent(WalkResponse.GROUP).isFinish == false)
							LogError("Движение - дошли, но пакет так с координатами так и не пришел"+(extrapolation?" и была экстраполяция расстоянием":""));
						else
							LogWarning("Движение - дошли");

						// если экстраполировали расстоянием то остаемся в тех координатах куда мы прошли чуть больше, что бы не отбрасывало назад (на координаты сервера)
						if(!extrapolation)
							transform.localPosition = finish;

						break;
					}
				}

				//LogError("Движение - Оставшееся время: "+GetEventRemain(WalkResponse.GROUP));

				activeLast = DateTime.Now;
				transform.localPosition = Vector3.MoveTowards(transform.localPosition, finish, (float)distancePerUpdate);

				yield return new WaitForFixedUpdate();
			}

			Log("Движение - завершена корутина движения");

			coroutines.Remove("walk");
			yield break;
		}

		/// <summary>
		/// корутина которая удаляет тз игры объект (если такая команда пришла с сервера). можно переопределить что бы изменить время удаления (0.5 секунда по умолчанию)
		/// </summary>
		private IEnumerator Remove(bool change_map)
		{
			if (change_map)
			{
				Log("Отложенное удаление при смене карты");
				DateTime start = DateTime.Now.AddSeconds(5);

				while (DateTime.Compare(start, DateTime.Now) >= 1)
				{
					// если спустя паузу мы все еще на той же карте - удалим объект (это сделано для плавного реконекта при переходе на карту ДРУГИМИ игроками)
					if (action != ConnectController.ACTION_REMOVE)
					{
						Log("Существо сменило статус с удаляемого на " + action + ", удаление отменено");
						yield break;
					}

					yield return new WaitForFixedUpdate();
				}
				Log("Существо так и не перешло на новую карту");
			}

			StartCoroutine(this.Destroy());
		}

		/// <summary>
		/// анимированное удаление объекта с карты (например когда снаряд попал в цель или игрок уходит с карты и др у кого есть анимация ACTION_REMOVE)
		/// </summary>
		protected override IEnumerator Destroy()
		{
			if (animator != null)
			{
				Log("Запуск анимации удаления с карты");

				Animate(animator, animator.GetLayerIndex(ConnectController.ACTION_REMOVE));
				yield return new WaitForSeconds(animator.GetCurrentAnimatorStateInfo(0).length - 0.01f);
			}
			Log("немедленое удаление с карты");
			Destroy(gameObject);
		}
	}
}
