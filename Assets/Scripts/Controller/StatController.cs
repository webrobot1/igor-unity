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
	/// Класс для обновления статистика соединения
	/// </summary>
    abstract public class StatController : UpdateController
    {
        private float deltaTime;

        [Header("Для работы с выводимой статистикой соединения")]
        [SerializeField]
        private Text ping;

        [SerializeField]
        private Text fps;

        [SerializeField]
        private Text map;

        protected override void Awake()
        {
            base.Awake();

            if (ping == null)
                Error("не присвоен Text для статистики пинга");

            if (fps == null)
                Error("не присвоен Text для статистики fps");          
            
            if (fps == null)
                Error("не присвоен Text для вывода номера карты");
        }

        protected override void Update()
        {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
            float fps = 1.0f / deltaTime;
            this.fps.text = "FPS: " + Mathf.Ceil(fps).ToString();

            base.Update();
        }

        protected virtual void HandleData(NewRecive<PlayerRecive, EnemyRecive, ObjectRecive> recive)
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