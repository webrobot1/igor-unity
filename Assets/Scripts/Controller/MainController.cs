using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
	/// Класс верхнего уровня. Служит в том числе для обновления статистика соединения
	/// </summary>
    public class MainController : WorldMapController
    {
        private float deltaTime;

        /// <summary>
        /// Как часто пересобирается строка счётчиков. Не каждый кадр по двум причинам: число, меняющееся
        /// шестьдесят раз в секунду, глазом не читается вовсе, а сборка самой строки выделяет память — и
        /// попала бы в тот самый счётчик мусора, что рядом и показан. Замер при этом идёт КАЖДЫЙ кадр,
        /// раз в период меняется только надпись.
        /// </summary>
        private const float COUNTERS_PERIOD = 0.25f;

        private float countersAge;

        /// <summary>
        /// Сглаженный мусор за кадр, байт. Сырое значение скачет (пришёл пакет — всплеск), и прочесть по
        /// нему нельзя ничего; сглаживаем тем же способом, что и время кадра.
        /// </summary>
        private float garbage;

        /// <summary>
        /// Размер управляемой кучи на прошлом замере — им считается прирост, когда счётчика движка нет.
        /// 0 — замеров ещё не было либо блок был скрыт (прирост между показами не наш).
        /// </summary>
        private long heapLast;

        /// <summary>
        /// Счётчик движка «выделено за кадр». Точен и считает только главный поток, но есть не во всякой
        /// сборке — недоступен, обходимся приростом кучи (см. <see cref="SampleGarbage"/>).
        /// </summary>
        private ProfilerRecorder allocated;

        [Header("Для работы с выводимой статистикой соединения")]
        [SerializeField]
        private Text ping;

        [SerializeField]
        private Text fps;

        [SerializeField]
        private Text map;

        /// <summary>
        /// Время кадра в миллисекундах. Стоит рядом с FPS, а не вместо него, потому что мерят правки
        /// именно им: FPS упирается в потолок из настройки игрока и запаса не показывает.
        /// </summary>
        [SerializeField]
        private Text frameTime;

        /// <summary>Сколько памяти выделено за кадр — по нему видно лишнюю работу в покадровом коде.</summary>
        [SerializeField]
        private Text garbageLabel;

        /// <summary>Сколько сущностей сейчас в мире: делитель, без него замеры несравнимы.</summary>
        [SerializeField]
        private Text entities;

        /// <summary>
        /// Имя слоя-земли карты, на которой стоит игрок: им задаётся порядок отрисовки сущностей и граница
        /// Chunk-режима у слоёв (см. MapDecode.spawn). Разбирающему карту нужно видеть, какой слой им стал.
        /// </summary>
        [SerializeField]
        private Text ground;

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
                    _instance = FindFirstObjectByType<MainController>();
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

            if (map == null)
            {
                Error("не присвоен Text для вывода номера карты");
                return;
            }

            if (frameTime == null || garbageLabel == null || entities == null || ground == null)
            {
                Error("не присвоены Text счётчиков времени кадра, мусора, числа сущностей либо слоя-земли");
                return;
            }

            _instance = this;

            allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");

            // До прихода настроек игрока блок счётчиков скрыт: обычному игроку он не нужен вовсе.
            ping.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>
        /// Запись профайлера держит неуправляемую память и сама собой не освобождается — снимаем явно.
        /// </summary>
        private void OnDestroy()
        {
            if (allocated.Valid)
                allocated.Dispose();
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

            bool shown = fps.transform.parent.gameObject.activeSelf;

            // Название карты видно в обычной игре (подпись у радара) — считаем всегда, независимо от
            // служебного блока. Пока блок скрыт, его подписи не пересчитываем — их никто не видит.
            MapLabel(shown);

            if (shown)
            {
                // Мусор снимаем КАЖДЫЙ кадр: пропущенный выпал бы из среднего, и оно бы врало. Счётчик
                // движка точен и считает только главный поток; его нет — берём прирост управляемой кучи
                // сами. Он грубее (туда попадает и приём пакетов фоновым потоком), а сборка мусора даёт
                // прирост ОТРИЦАТЕЛЬНЫЙ — такой кадр в среднее не берём, иначе оно уползало бы вниз.
                long bytes;

                if (allocated.Valid)
                    bytes = allocated.LastValue;
                else
                {
                    long heap = GC.GetTotalMemory(false);
                    bytes = heapLast > 0 ? heap - heapLast : 0;
                    heapLast = heap;
                }

                if (bytes >= 0)
                    garbage += (bytes - garbage) * 0.1f;

                // Надпись пересобирается раз в COUNTERS_PERIOD, чтобы её саму было и видно, и не слышно.
                countersAge += Time.unscaledDeltaTime;

                if (countersAge >= COUNTERS_PERIOD)
                {
                    countersAge = 0f;

                    // Сущности живут внутри зон карт. Игроки считаются отдельной строкой: нагрузку от них
                    // и от прочих существ разводят разные причины (чужие игроки приходят и уходят сами,
                    // мобов ставит карта), и по общему числу их вклад не разделить.
                    int count = 0;
                    int players = 0;

                    foreach (Transform zone in worldObject.transform)
                    {
                        count += zone.childCount;

                        foreach (Transform entity in zone)
                            if (entity.GetComponent<PlayerModel>() != null)
                                players++;
                    }

                    fps.text = "FPS: " + Mathf.Ceil(1f / deltaTime);
                    frameTime.text = "Кадр: " + (deltaTime * 1000f).ToString("0.0") + " мс";
                    garbageLabel.text = "Мусор: " + (garbage / 1024f).ToString("0.0") + " КБ/кадр";
                    entities.text = "Существ: " + (count - players) + " | Игроков: " + players;
                }
            }
            else
                heapLast = 0;

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

            // Слой-земля: имя показываем только когда его назвала сама карта и такой слой у неё нашёлся.
            // Иначе слой выбран не картой — клиент подставил запасной индекс, и это должно быть видно, а не
            // выглядеть заданным значением (см. MapDecode.spawn).
            string layer = "Земля: " + (decoded == null
                ? "карта не загружена"
                : string.IsNullOrEmpty(decoded.spawn)
                    ? "не задана, запасной слой " + decoded.spawn_sort
                    : decoded.spawn);

            if (ground.text != layer)
                ground.text = layer;
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
