using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MyFantasy
{
    public class PlayerController : UpdateController
    {
        public Text ping;

        public static new NewPlayerModel player
        {
            get { return (NewPlayerModel)ConnectController.player; }
            set { ConnectController.player = value; }
        }
        public static NewEnemyModel target = null;

        protected override void Start()
        {
            if (ping == null)
                Error("не присвоен GameObject для статистики пинга");

            if (Camera.main.GetComponent<CameraController>().hpFrame == null)
                Error("не присвоен GameObject для линии жизни");

            if (Camera.main.GetComponent<CameraController>().mpFrame == null)
                Error("не присвоен GameObject для линии маны");
        }

        protected virtual void FixedUpdate()
        {

            if (target != null)
            {
                if (target.hp == 0) Select(null);
            }

            if (target == null && player.getEvent("attack").action!=null && player.getEvent("attack").action != "")
            {
                // если с севрера пришло что нас кто то атакует и мы сами никого не атакуем
                string new_target = player.getEventData<AttackDataRecive>("attack").target;
                if (new_target!=null)
                {
                    Select(new_target);
                    Debug.LogWarning("Новая цель атаки с сервера: "+ new_target);
                }
            }
        }

        public static void Select(string key = null)
        {
            if (target != null)
                target.transform.Find("LifeBar").GetComponent<CanvasGroup>().alpha = 0;

            if (key != null)
            {
                GameObject gameObject = GameObject.Find(key);
                if (gameObject != null)
                {
                    NewEnemyModel new_target = gameObject.GetComponent<NewEnemyModel>();
                    if (new_target != null && new_target.hp > 0) 
                    {
                        target = new_target;
                        new_target.transform.Find("LifeBar").GetComponent<CanvasGroup>().alpha = 1;
                    }
                    else
                        target = null;
                }
            }
            else
                target = null;
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

        public override void Connect(SigninRecive data)
        {
            base.Connect(data);
        }
    }
}