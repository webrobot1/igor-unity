using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
	/// Класс верхнего уровня. Служит в том числе для обновления статистика соединения
	/// </summary>
    public class MainController : DebugPanelController
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

            // До прихода настроек игрока блок счётчиков скрыт: обычному игроку он не нужен вовсе.
            ping.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>
        /// Блок счётчиков (частота, задержка, номер карты) — служебный: показываем только в тестовом режиме.
        /// Ссылка на сам блок — родитель этих подписей, отдельного поля не заводим.
        /// </summary>
        protected override void SetTestMode(bool enabled)
        {
            base.SetTestMode(enabled);

            if (ping != null)
                ping.transform.parent.gameObject.SetActive(enabled);
        }

        protected override void Update()
        {
            deltaTime += (Time.deltaTime - deltaTime) * 0.1f;
            float fps = 1.0f / deltaTime;

            // Пока блок скрыт (обычный режим игры), подписи не пересчитываем — их всё равно никто не видит.
            if (this.fps.transform.parent.gameObject.activeSelf)
                this.fps.text = "FPS: " + Mathf.Ceil(fps).ToString();

            base.Update();
        }

        protected override void HandleData(NewRecive<PlayerRecive, EnemyRecive> recive)
        {
            base.HandleData(recive);
            if (recive.unixtime > 0)
                ping.text = "PING: " + Ping() * 1000 + "/" + MaxPing() * 1000 + " ms.";
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
            if (key == player_key)
            {
                if (recive.map != null)
                    map.text = "Карта: " + recive.map;
            }

            GameObject go = base.UpdateObject(map_id, key, recive);

            // Маяк-подсветка на подбираемых предметах, лежащих в мире (kind=item / экипируемые). Решаем
            // ЗДЕСЬ, в вызывающем: подходит ли сущность под подсветку — ответственность места, где она
            // обрабатывается, а не самого маркера (маркер вешается только когда уже решено, что он нужен).
            // recive.prefab непуст только в полном пакете спавна и при смене prefab (на дельтах == null),
            // поэтому IsGroundItem не считается каждый кадр. EquipableGroundMarker — игровой слой
            // (Assembly-CSharp), поэтому триггерим здесь, а не во фреймворчном UpdateController (firstpass его не видит).
            if (go != null && !string.IsNullOrEmpty(recive.prefab))
            {
                if (AnimationCacheService.IsGroundItem(recive.prefab))
                    EquipableGroundMarker.Apply(go);

                // Существа подписываются именем под курсором (у игрока — логин, у моба — название из
                // library). Декор и снаряды (kind=object) — нет: имени у них по смыслу нет, а курсор
                // над каждым кустом давал бы плашку. Вещам надпись даёт маркер выше — своим видом.
                else if (go.GetComponent<EntityModel>() is EntityModel model && model.type != "object")
                    HoverLabel.Apply(go, HoverLabel.PrefabCreature);
            }

            return go;
        }
    }
}
