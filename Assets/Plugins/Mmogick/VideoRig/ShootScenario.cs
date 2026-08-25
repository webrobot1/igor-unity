using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Mmogick.VideoRig
{
    /// <summary>
    /// Сценарий съёмки: что персонаж делает перед камерой и по каким границам режется запись. Ролик
    /// собирается из фрагментов — на каждую сцену закадрового текста свой файл, — а текст и озвучка
    /// меняются от правки к правке, поэтому съёмку надо повторять один в один. Отсюда сценарий и лежит
    /// ДАННЫМИ, а не кодом: правится он без пересборки клиента и переживает фоновый запуск редактора.
    ///
    /// Формат — JSON, разбор строгий: незнакомое поле роняет загрузку. Опечатка в имени действия либо
    /// цели иначе прошла бы молча, а обнаружилась бы уже снятым не тем фрагментом.
    /// </summary>
    public class ShootScenario
    {
        /// <summary>Имя сценария. Каталогом вывода не является — его задаёт запускающий.</summary>
        public string name;

        /// <summary>
        /// Размер кадра и частота. Ролик собирается в 1920×1080 при 30 кадрах/с; фрагмент меньшего
        /// размера сборщик растянет, и игровые сцены выйдут мягче соседних кадров админки.
        /// </summary>
        public int width = 1920;

        public int height = 1080;

        public float fps = 30f;

        public List<ShootScene> scenes;

        public static ShootScenario Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("сценарий съёмки не найден: " + path);

            ShootScenario scenario = JsonConvert.DeserializeObject<ShootScenario>(
                File.ReadAllText(path),
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });

            if (scenario == null || scenario.scenes == null || scenario.scenes.Count == 0)
                throw new InvalidOperationException("сценарий " + path + " не объявляет ни одной сцены");

            HashSet<string> ids = new HashSet<string>();

            foreach (ShootScene scene in scenario.scenes)
            {
                if (string.IsNullOrEmpty(scene.id))
                    throw new InvalidOperationException("у сцены сценария " + path + " нет идентификатора: им зовётся файл фрагмента");

                if (!ids.Add(scene.id))
                    throw new InvalidOperationException("сцена " + scene.id + " объявлена дважды: фрагменты перетёрли бы друг друга");

                if (scene.actions == null || scene.actions.Count == 0)
                    throw new InvalidOperationException("сцена " + scene.id + " не объявляет ни одного действия");
            }

            return scenario;
        }
    }

    /// <summary>Сцена ролика: свой файл записи «идентификатор.mp4» и список действий персонажа.</summary>
    public class ShootScene
    {
        public string id;

        public List<ShootAction> actions;
    }

    /// <summary>
    /// Действие сценария. Вид задаёт <see cref="@do"/>:
    ///
    /// wait     — пауза на seconds.
    /// pointer  — вести указатель к цели за seconds (по умолчанию — своя длительность ведения).
    /// click    — то же ведение (если цель задана) и нажатие в конце. Нажатие идёт тем же путём, что
    ///            живое: через тот же источник ввода, который читают и игра, и EventSystem.
    /// walk_dir — держать направление движения (x, y) на seconds: путь клавиатурных осей.
    ///
    /// Цель у pointer и click задаётся РОВНО одним полем: screen, ui, bar, map либо entity.
    /// </summary>
    public class ShootAction
    {
        public string @do;

        public float seconds;

        /// <summary>Цель: точка кадра в пикселях, [x, y]. Начало — левый нижний угол, как у экранных координат движка.</summary>
        public float[] screen;

        /// <summary>Цель: объект интерфейса по имени (кнопка окна, ячейка) — берётся центр его области.</summary>
        public string ui;

        /// <summary>Цель: слот панели быстрых действий по его номеру.</summary>
        public int bar;

        /// <summary>Цель: клетка карты, [x, y] в серверных координатах — та же система, что у позиции сущности.</summary>
        public float[] map;

        /// <summary>Цель: сущность. "nearest_enemy" — ближайший живой враг; иначе ключ сущности.</summary>
        public string entity;

        /// <summary>Направление для walk_dir.</summary>
        public float x;

        public float y;
    }
}
