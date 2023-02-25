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
        public static new NewPlayerModel player
        {
            get { return (NewPlayerModel)ConnectController.player; }
            set { ConnectController.player = value; }
        }

        public Image hpFrame;
        public Image mpFrame;

        public Text ping;

        protected NewEnemyModel target = null;

        public static PlayerController Instance { get; private set; }
        protected override void Awake()
        {
            // If there is an instance, and it's not me, delete myself.
            if (hpFrame == null)
                Error("не присвоен GameObject для линии жизни");

            if (mpFrame == null)
                Error("не присвоен GameObject для линии маны");         
            
            if (ping == null)
                Error("не присвоен GameObject для статистики пинга");

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

        protected override void Update()
        {
            base.Update();

            if (target != null)
            {
                if (target.hp == 0) Select(null);
            }
        }

        protected void Select(NewEnemyModel new_target = null)
        {
            if (target != null)
                target.transform.Find("LifeBar").GetComponent<CanvasGroup>().alpha = 0;

            if (new_target != null && new_target.hp > 0)
                new_target.transform.Find("LifeBar").GetComponent<CanvasGroup>().alpha = 1;
            else
                new_target = null;

            target = new_target;
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