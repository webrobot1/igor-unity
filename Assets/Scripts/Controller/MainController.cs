using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MyFantasy
{
    /// <summary>
	/// Класс верхнего уровня. Служит в том числе для обновления статистика соединения
	/// </summary>
    public class MainController : CursorController
    {
        private float deltaTime;

        [Header("Для работы с выводимой статистикой соединения")]
        [SerializeField]
        private Text ping;

        [SerializeField]
        private Text fps;

        [SerializeField]
        private Text map;

        /// <summary>
        /// Singleton instance of the handscript
        /// </summary>
        private static MainController _instance;

        public static MainController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<MainController>();
                }

                return _instance;
            }
        }
        protected override void Awake()
        {
            base.Awake();

            if (ping == null)
            {
                Error("не присвоен Text для статистики пинга");
                return;
            }

            if (fps == null)
            {
                Error("не присвоен Text для статистики fps");
                return;
            }
                             
            if (fps == null)
            {
                Error("не присвоен Text для вывода номера карты");
                return;
            }

            _instance = this;
        }

        protected override void Update()
        {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
            float fps = 1.0f / deltaTime;
            this.fps.text = "FPS: " + Mathf.Ceil(fps).ToString();

            base.Update();
        }

        protected override void HandleData(NewRecive<PlayerRecive, EnemyRecive, ObjectRecive> recive)
        {
            base.HandleData(recive);
            if (recive.unixtime > 0)
                ping.text = "PING: " + Ping() * 1000 + "/" + MaxPing() * 1000 + " ms.";
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            if (key == player_key)
            {
                if (recive.map_id != null)
                    map.text = "Карта: " + recive.map_id;
            }

            return base.UpdateObject(map_id, key, recive, type);
        }
    }
}