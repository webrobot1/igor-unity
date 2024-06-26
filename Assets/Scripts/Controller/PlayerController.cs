using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
	/// Класс для обновления Frame UI игрока его цели атаки
	/// </summary>
    abstract public class PlayerController : UpdateController
    {
        [Header("Для работы с иконкой персонажа и его цели")]
        [SerializeField]
        private TargetController _playerFaceController;  
        
        [SerializeField]
        private TargetController _targetFaceController;

        /// <summary>
        ///  это цель которую мы выбрали сами , не автоматическая
        /// </summary>
        protected bool persist_target;

        /// <summary>
        ///  переопределим свйоство игрока да так что бы и вродительском оставался доступен
        ///  Todo кроме cameraContoller и FaceController не используется и то оттуда можно убрать перенеся функционал сюда и сделав protected это свойство
        /// </summary>
        public static PlayerModel Player
        {
            get 
            { 
                if (player != null) 
                    return (PlayerModel)player;
                else 
                    return null; 
            }
        }

        public ObjectModel Target 
        {
            get { return _targetFaceController.Target; } 
            set 
            {
                if (player != null && value != null && value.key != player.key)
                    _targetFaceController.Target = value; 
                else
                    _targetFaceController.Target = null;
            } 
        }

        protected override void Awake()
        {
            base.Awake();

            if (_playerFaceController == null)
            {
                Error("не присвоен фрейм жизней игрока");
                return;
            }
                      
            
            if (_targetFaceController == null)
            {
                Error("не присвоен фрейм жизней цели");
                return;
            }               
        }


        /// <summary>
        /// в целом два следующих метода нужны тошько если вы переделваете стандыртный форат ответа тк из коробки у всех сущностей одни данные (кроме логина у пользователей, а компоненты и события из универсального object можно после в любые превратить оьъекты классов)
        /// ну может в recive что то решите добаить новое...типа новостей для расылки игркоам или еще какие то глобальные
        /// </summary>
        protected override void Handle(string json)
        {
            HandleData(JsonConvert.DeserializeObject<NewRecive<PlayerRecive, EnemyRecive, ObjectRecive>>(json));
        }

        protected virtual void HandleData(NewRecive<PlayerRecive, EnemyRecive, ObjectRecive> recive)
        {
            // после ACTION_LOAD старые объекты будут заменены новыми объектами клонами и надо сохранить все ключи что нам нужно будет залинковать с игроком (напрмиер цель)
            string tmp_target = null;
            if (recive.action == ACTION_LOAD)
            {
                if (_targetFaceController.Target != null)
                {
                    tmp_target = _targetFaceController.Target.key;
                }
            }

            base.HandleData(recive);

            if (_playerFaceController.Target == null && player != null)
            {
                player.Log("Инициализация фрейма игрока");

                // установим иконку нашего персонажа в превью и свяжем его анимацию с ней
                // именно Player - новый объект нашего игрока унаследованного от объекта плагина сервера 
               _playerFaceController.Target = Player;
            }

            if (recive.action == ACTION_LOAD && tmp_target != null && Target == null)
            {
                player.LogError("Потерялась цель игрока при загрузке " + tmp_target);
                GameObject gameObject = GameObject.Find(tmp_target);
                if (gameObject == null)
                {
                    _targetFaceController.Target = null;
                }
                else
                {
                    player.LogError("Цель была найдена снова " + tmp_target);
                    _targetFaceController.Target = gameObject.GetComponent<ObjectModel>();
                }
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            GameObject prefab = base.UpdateObject(map_id, key, recive, type);
            if (player != null && prefab!=null)
            {
                ObjectModel model = prefab.GetComponent<ObjectModel>();
                if (recive.events != null)
                {
                    if (key == player_key)
                    {
                        if (recive.events.ContainsKey(AttackResponse.GROUP))
                        {
                            // мы можем переопределить цель если мы ее сами не выбрали или не нацелены на безжизненное существо или мертвое существо
                            // если с севрера пришло что мы кого то атакуем мы вынуждены переключить цель и не важно кого хочет игрок атаковать
                            string attacker = player.getEventData<AttackDataRecive>(AttackResponse.GROUP).target;

                            if (attacker != null)
                            {
                                GameObject gameObject = GameObject.Find(attacker);
                                if (gameObject != null)
                                {
                                    ObjectModel attackerModel = gameObject.GetComponent<EnemyModel>();
                                    if (attackerModel != null && CanBeTarget(attackerModel))
                                    {
                                        Target = attackerModel;
                                        player.Log("Новая цель атаки с сервера: " + attacker);
                                    }
                                }
                                else
                                    player.LogError("Цель " + attacker + " не найдена на сцене");
                            }
                        }
                    }
                    else if (recive.events.ContainsKey(AttackResponse.GROUP))
                    {
                        // если существо атакует игрока и игроку можно установить эту цель (подробнее в функции SelectTarget) - установим
                        if (
                            CanBeTarget(model)
                                &&
                            model.getEventData<AttackDataRecive>(AttackResponse.GROUP).target == player.key)
                        {
                            // то передадим инфомрацию игроку что бы мы стали его целью
                            Target = model;
                            player.LogWarning("Сущность " + key + " атакует нас, установим ее как цель цель");
                        }
                    }
                }
            }
         
            return prefab;
        }

        private bool CanBeTarget(ObjectModel gameObject)
        {
            return
            (
                Vector3.Distance(player.transform.position, gameObject.transform.position) < player.lifeRadius
                    &&
                (
                    Target == null
                         ||
                     (
                         Target.key != gameObject.key
                             &&
                         (
                             (!persist_target && Vector3.Distance(Target.position, player.position) > Vector3.Distance(gameObject.transform.position, player.position))
                                 ||
                             ((EnemyModel)Target).hp == null
                                 ||
                             ((EnemyModel)Target).hp == 0
                         )
                     )
                 )
             );
        }
    }
}