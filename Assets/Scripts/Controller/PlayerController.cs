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
        private FaceAnimationController playerFaceController;       
        [SerializeField]
        private FaceAnimationController targetFaceController;

        /// <summary>
        ///  переопределим свйоство игрока да так что бы и вродительском оставался доступен
        /// </summary>
        public new NewPlayerModel player
        {
            get { return (NewPlayerModel)ConnectController.player; }
        }

        private NewObjectModel _target;
        protected NewObjectModel target 
        {
            get { return _target; } 
            private set { _target = value; } 
        }

        /// <summary>
        ///  это цель которую мы выбрали сами , не автоматическая
        /// </summary>
        private bool persist_target;

        public static PlayerController Instance { get; private set; }
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

            Application.targetFrameRate = 300;
           // Screen.orientation = ScreenOrientation.LandscapeLeft;

            base.Awake();
        }

        protected override void Start()
        {
            SelectTarget(null);

            if (ping == null)
                Error("не присвоен фрейм для статистики пинга");          
            
            if (playerFaceController == null)
                Error("не присвоен фрейм жизней игрока");          
            
            if (targetFaceController == null)
                Error("не присвоен фрейм жизней цели");
        }

        protected override void Update()
        {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
            float fps = 1.0f / deltaTime;
            this.fps.text = "FPS: " + Mathf.Ceil(fps).ToString() + " мс.";

            base.Update();
        }

        protected virtual void FixedUpdate()
        {
            if (player != null) 
            { 
               // если объект (может живой может нет) очень далеко снимем таргет (не важно мы ли его поставили или автоматом)
               if (target != null && Vector3.Distance(player.transform.position, target.transform.position) >= player.lifeRadius)
                   SelectTarget(null);

                // мы можем переопределить цель если мы ее сами не выбрали или не нацелены на безжизненное существо или мертвое существо
                // если с севрера пришло что мы кого то атакуем мы вынуждены переключить цель и не важно кого хочет игрок атаковать
                string attacker = player.getEventData<AttackDataRecive>(AttackResponse.GROUP).target;
                if (attacker != null)
                {
                    NewEnemyModel gameObject = GameObject.Find(attacker).GetComponent<NewEnemyModel>();
                    if (gameObject != null && CanBeTarget(gameObject)) 
                    { 
                        SelectTarget(gameObject);
                        Debug.LogWarning("Новая цель атаки с сервера: "+ attacker);
                    }
                }
            }
        }

        protected override void SetPlayer(ObjectModel player)
        {
            base.SetPlayer(player);
            playerFaceController.target = (NewEnemyModel)player;
        }

        public bool CanBeTarget(NewObjectModel gameObject)
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

        public void SelectTarget(NewObjectModel new_target, bool persist = false)
        {
            if (player!=null && new_target != null && new_target.key != player.key)
            {
                // если дальше чем поле видимости игрока то не ставим выделение (может там другой игрок ходит рядом и существо продолжило идти к игроку поэтому)
                if (Vector3.Distance(player.transform.position, new_target.transform.position) < player.lifeRadius)
                {
                    targetFaceController.target = target = new_target;
                    persist_target = persist;
                }
            }
            else 
            {
                targetFaceController.target = target = null;
                persist_target = false;
            }
        }

        protected override void Handle(string json)
        {
            HandleData(JsonConvert.DeserializeObject<NewRecive<NewPlayerRecive, NewEnemyRecive, NewObjectRecive>>(json));
        }

        protected void HandleData(NewRecive<NewPlayerRecive, NewEnemyRecive, NewObjectRecive> recive)
        {
            base.HandleData(recive);

            if(recive.unixtime>0)
                ping.text = "PING: "+ Ping() * 1000+" мс.";
        }
    }
}