using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MyFantasy
{
    abstract public class PlayerController : UpdateController
    {
        [SerializeField]
        private Text ping;      
        [SerializeField]
        private Text fps;
        private float deltaTime;


        [SerializeField]
        private TargetController playerFaceController;       
        [SerializeField]
        private TargetController targetFaceController;

        /// <summary>
        ///  переопределим свйоство игрока да так что бы и вродительском оставался доступен
        ///  Todo кроме cameraContoller и FaceController не используется и то оттуда можно убрать перенеся функционал сюда и сделав protected это свойство
        /// </summary>
        public new NewPlayerModel player
        {
            get { return (NewPlayerModel)ConnectController.player; }
        }

        protected NewObjectModel target 
        {
            get { return targetFaceController.target; } 
            set 
            {
                if (player != null && value != null && value.key != player.key)
                   targetFaceController.target = value; 
                else
                   targetFaceController.target = null;
            } 
        }

        /// <summary>
        ///  это цель которую мы выбрали сами , не автоматическая
        /// </summary>
        protected bool persist_target;

        public static new PlayerController Instance { get; private set; }

        protected override void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
            }
            else
            {
                Instance = this;
            }

            base.Awake();
        }

        protected override void Start()
        {
            target = null;

            if (ping == null)
                Error("не присвоен фрейм для статистики пинга");          
            
            if (playerFaceController == null)
                Error("не присвоен фрейм жизней игрока");          
            
            if (targetFaceController == null)
                Error("не присвоен фрейм жизней цели");

            // скроем наши заплатки (там тестовые иконки выделенного персонажа и врага)
            playerFaceController.target = targetFaceController.target = null;
        }

        protected override void Update()
        {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
            float fps = 1.0f / deltaTime;
            this.fps.text = "FPS: " + Mathf.Ceil(fps).ToString();

            base.Update();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
        }


        /// <summary>
        /// в целом два следующих метода нужны тошько если вы переделваете стандыртный форат ответа тк из коробки у всех сущностей одни данные (кроме логина у пользователей, а компоненты и события из универсального object можно после в любые превратить оьъекты классов)
        /// ну может в recive что то решите добаить новое...типа новостей для расылки игркоам или еще какие то глобальные
        /// </summary>
        protected override void Handle(string json)
        {
            HandleData(JsonConvert.DeserializeObject<NewRecive<NewPlayerRecive, NewEnemyRecive, NewObjectRecive>>(json));
        }

        protected void HandleData(NewRecive<NewPlayerRecive, NewEnemyRecive, NewObjectRecive> recive)
        {
            // после ACTION_LOAD старые объекты будут заменены новыми объектами клонами и надо сохранить все ключи что нам нужно будет залинковать с игроком (напрмиер цель)
            string tmp_target = null;
            if (recive.action == ACTION_LOAD)
            {
                if (target != null)
                {
                    tmp_target = target.key;
                }
            }
     
            base.HandleData(recive);

            if (playerFaceController.target == null && player != null)
            {
                Debug.Log("Инициализация фрейма игрока");

                // установим иконку нашего персонажа в превью и свяжем его анимацию с ней
                playerFaceController.target = player;
            }

            if (recive.action == ACTION_LOAD && tmp_target != null && target == null)
            {
                Debug.LogError("Потерялась цель игрока при загрузке " + tmp_target);
                GameObject gameObject = GameObject.Find(tmp_target);
                if (gameObject == null)
                {
                    target = null;
                }
                else
                {
                    Debug.LogError("Цель была найдена снова " + tmp_target);
                    target = gameObject.GetComponent<NewObjectModel>();
                }
            }
           
            if (recive.unixtime > 0)
                ping.text = "PING: " + Ping() * 1000 + " ms."; 
        }

        protected override GameObject UpdateObject(int map_id, string key, ObjectRecive recive, string type)
        {
            NewObjectModel model = base.UpdateObject(map_id, key, recive, type).GetComponent<NewObjectModel>();

            if (player!=null)
            {
                if (key == player.key)
                {
                    if (recive.events!=null && recive.events.ContainsKey(AttackResponse.GROUP))
                    {
                        // мы можем переопределить цель если мы ее сами не выбрали или не нацелены на безжизненное существо или мертвое существо
                        // если с севрера пришло что мы кого то атакуем мы вынуждены переключить цель и не важно кого хочет игрок атаковать
                        string attacker = player.getEventData<AttackDataRecive>(AttackResponse.GROUP).target;

                        if (attacker != null) 
                        {
                            GameObject gameObject = GameObject.Find(attacker);
                            if (gameObject!=null)
                            {
                                NewObjectModel attackerModel = gameObject.GetComponent<NewEnemyModel>();
                                if(attackerModel!=null && CanBeTarget(attackerModel)) 
                                { 
                                    target = attackerModel;
                                    Debug.Log("Новая цель атаки с сервера: " + attacker);
                                }
                            }
                            else
                                Debug.LogError("Цель "+ attacker + " не найдена на сцене");
                        }
                    }
                }
                else if(recive.events != null && recive.events.ContainsKey(AttackResponse.GROUP))
                {
                    // если существо атакует игрока и игроку можно установить эту цель (подробнее в функции SelectTarget) - установим
                    if (
                        CanBeTarget(model)
                            &&
                        model.getEventData<AttackDataRecive>(AttackResponse.GROUP).target == player.key)
                    {
                        // то передадим инфомрацию игроку что бы мы стали его целью
                        target = model;
                        Debug.LogWarning("Сущность " + key + " атакует нас, установим ее как цель цель");
                    }
                }
            }

            return model.gameObject;
        }

        private bool CanBeTarget(NewObjectModel gameObject)
        {
            return
            (
                Vector3.Distance(player.transform.position, gameObject.transform.position) < player.lifeRadius
                    &&
                (
                    target == null
                         ||
                     (
                         target.key != gameObject.key
                             &&
                         (
                             (!persist_target && Vector3.Distance(target.position, player.position) > Vector3.Distance(gameObject.transform.position, player.position))
                                 ||
                             target.hp == null
                                 ||
                             target.hp == 0
                         )
                     )
                 )
             );
        }
    }
}