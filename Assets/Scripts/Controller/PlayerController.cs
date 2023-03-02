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

        private NewEnemyModel _target;
        public NewEnemyModel target 
        {
            get { return _target; } 
            private set { _target = value; } 
        }
     
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
               if (target != null && (target.hp == 0 || Vector3.Distance(player.transform.position, target.transform.position) >= player.lifeRadius))
                   SelectTarget(null);

                if (target == null && player!=null && player.getEvent(AttackResponse.GROUP).action!=null && player.getEvent(AttackResponse.GROUP).action != "")
                {
                    // если с севрера пришло что мы кого то атакуем
                    string new_target = player.getEventData<AttackDataRecive>(AttackResponse.GROUP).target;
                    if (new_target!=null)
                    {
                        SelectTarget(new_target);
                        Debug.LogWarning("Новая цель атаки с сервера: "+ new_target);
                    }
                }
            }
        }

        protected override void SetPlayer(ObjectModel player)
        {
            base.SetPlayer(player);
            playerFaceController.target = (NewEnemyModel)player;
        }

        public void SelectTarget(string key = null)
        {
            if (player!=null && key != null && key != player.key)
            {
                GameObject gameObject = GameObject.Find(key);
                if (gameObject != null)
                {
                    NewEnemyModel new_target = gameObject.GetComponent<NewEnemyModel>();
                    if (new_target != null && new_target.hp > 0 && Vector3.Distance(player.transform.position, new_target.transform.position) < player.lifeRadius) 
                    {
                        targetFaceController.target = target = new_target;
                    }
                }
            }
            else 
            {
                targetFaceController.target = target = null;
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