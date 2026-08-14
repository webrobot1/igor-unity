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
    public class MainController : InfoWindowController
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

            // Название карты видно в обычной игре (подпись у радара) — считаем всегда, независимо от
            // служебного блока. Пока блок скрыт, его подписи не пересчитываем — их никто не видит.
            MapLabel(this.fps.transform.parent.gameObject.activeSelf);

            if (this.fps.transform.parent.gameObject.activeSelf)
                this.fps.text = "FPS: " + Mathf.Ceil(fps).ToString();

            base.Update();
        }

        /// <summary>
        /// Подписи карты: номер адресует её в инструментах (служебный блок), название говорит игроку,
        /// где он находится (подпись у радара). Собираются каждый кадр, а не по приходу пакета игрока:
        /// карта грузится асинхронно и название приезжает вместе с ней — по пакету оно встало бы только
        /// у той карты, что успела загрузиться.
        /// </summary>
        /// <param name="withNumber">Служебный блок показан — обновляем и подпись с номером карты.</param>
        private void MapLabel(bool withNumber)
        {
            // До спавна игрока карты нет — подпись пустая, иначе в кадрах загрузки висит образец из сцены.
            if (PlayerController.Player == null)
            {
                SetMapName("");
                return;
            }

            int mapId = PlayerController.Player.map;

            MapDecode decoded;
            SetMapName(getMaps().TryGetValue(mapId, out decoded) && !string.IsNullOrEmpty(decoded.name)
                ? decoded.name
                : "");

            if (!withNumber)
                return;

            string label = "Карта: " + mapId;
            if (map.text != label)
                map.text = label;
        }

        protected override void HandleData(NewRecive<PlayerRecive, CreatureRecive> recive)
        {
            base.HandleData(recive);
            if (recive.unixtime > 0)
                ping.text = "PING: " + Ping() * 1000 + "/" + MaxPing() * 1000 + " ms.";
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
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
