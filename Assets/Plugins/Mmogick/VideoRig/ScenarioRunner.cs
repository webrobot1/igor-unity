using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick.VideoRig
{
    /// <summary>
    /// Исполнитель сценария съёмки: ведёт указатель, нажимает, водит персонажа и режет запись по границам
    /// сцен. Живёт только в редакторе и только пока идёт прогон.
    ///
    /// Шаги идут корутиной в такт кадрам игры — ввод игра читает раз в кадр, и нажатие короче кадра не
    /// увидел бы никто. Крутит корутину объект-носитель <see cref="ScenarioHost"/>: собственным
    /// компонентом сцены исполнитель быть не может, он из редакторной сборки.
    ///
    /// Ввод отдаётся сценарию целиком (<see cref="InputSource"/> плюс <see cref="ScenarioPointerInput"/>
    /// у EventSystem): движения руки человека в запись попадать не должны. Отказ на любом шаге —
    /// немедленная остановка со снятым перехватом: снятое «наполовину» хуже неснятого, его легко принять
    /// за годный фрагмент.
    /// </summary>
    public sealed class ScenarioRunner
    {
        /// <summary>Сколько ведём указатель, когда сцена не сказала иначе.</summary>
        private const float POINTER_SECONDS = 0.5f;

        /// <summary>Подвес до первого и после последнего действия сцены: фрагмент не начинается в движении.</summary>
        private const float SCENE_PAD = 0.4f;

        /// <summary>Пауза после остановки записи — пакет дописывает файл.</summary>
        private const float FLUSH = 1f;

        /// <summary>Во сколько раз указатель в кадре крупнее системного: 32 пикселя на кадре 1080p мелки для ролика.</summary>
        private const float POINTER_SCALE = 1.5f;

        /// <summary>Сколько ждём мир после запуска: вход в игру сетевой, карта грузится не мгновенно.</summary>
        private const float WORLD_TIMEOUT = 90f;

        /// <summary>Ход прогона — его опрашивает снаружи тот, кто прогон запустил.</summary>
        public static string Status { get; private set; } = "не запускался";

        /// <summary>Снятые фрагменты последнего прогона.</summary>
        public static List<string> Files { get; private set; } = new List<string>();

        private ShootScenario scenario;

        private SceneRecorder recorder;

        private ScenarioPointerInput pointerInput;

        private BaseInputModule module;

        private BaseInput savedOverride;

        private string failure;

        private readonly GameObject host;

        internal ScenarioRunner(ShootScenario scenario, string outputDir, GameObject host)
        {
            this.scenario = scenario;
            this.host = host;

            recorder = new SceneRecorder(outputDir, scenario.width, scenario.height, scenario.fps);

            Files = new List<string>();
            Status = "запущен";
        }

        internal IEnumerator Play()
        {
            Status = "жду мир";
            yield return WaitWorld();

            if (failure == null)
                Attach();

            for (int i = 0; failure == null && i < scenario.scenes.Count; i++)
            {
                ShootScene scene = scenario.scenes[i];
                Status = "снимаю " + scene.id;

                string file;

                try
                {
                    file = recorder.Begin(scene.id);
                }
                catch (Exception ex)
                {
                    // Наружу уходит только текст, а разбираться придётся по месту отказа: без стека
                    // причина видна лишь по сообщению.
                    Debug.LogException(ex);
                    Fail("сцена " + scene.id + ": " + ex.Message);
                    break;
                }

                yield return new WaitForSeconds(SCENE_PAD);

                foreach (ShootAction action in scene.actions)
                {
                    yield return Do(action);

                    if (failure != null)
                        break;
                }

                yield return new WaitForSeconds(SCENE_PAD);

                recorder.End();
                Files.Add(file);

                yield return new WaitForSeconds(FLUSH);
            }

            Finish();
        }

        /// <summary>
        /// Сценарий начинается после входа в игру: до появления своего существа и камеры мира ни цели
        /// на карте, ни интерфейса ещё нет.
        ///
        /// Готовность спрашиваем у самой панели загрузки, а не у появления существа: панель снимается,
        /// когда карта вокруг игрока построена и его тело показано, — до того первые секунды фрагмента
        /// заняла бы заставка загрузки.
        /// </summary>
        private IEnumerator WaitWorld()
        {
            float deadline = Time.realtimeSinceStartup + WORLD_TIMEOUT;

            while (PlayerController.Player == null || Camera.main == null || MainController.Instance == null
                || LoadingScreen.IsShown)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Fail("мир не загрузился за " + WORLD_TIMEOUT + " с — вход в игру не состоялся");
                    yield break;
                }

                yield return null;
            }
        }

        private void Attach()
        {
            Texture2D texture = HandCursor.OpenTexture;

            if (texture == null)
            {
                Fail("не нашлась картинка указателя");
                return;
            }

            module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;

            if (module == null)
            {
                Fail("у сцены нет EventSystem с модулем ввода — нажатия сценария не дошли бы до интерфейса");
                return;
            }

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            // Точка попадания у картинки своя (у ладони — между пальцами), а положение рисуемой руки
            // задаётся её точкой опоры: переводим одну в другую. Отсчёт точки попадания идёт сверху, у
            // точки опоры — снизу.
            Vector2 pivot = new Vector2(
                HandCursor.OpenHotspot.x / texture.width,
                1f - HandCursor.OpenHotspot.y / texture.height);

            InputSource.BeginScript(
                sprite,
                new Vector2(texture.width, texture.height) * POINTER_SCALE,
                pivot,
                new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));

            pointerInput = module.gameObject.AddComponent<ScenarioPointerInput>();
            savedOverride = module.inputOverride;
            module.inputOverride = pointerInput;

            Debug.Log("VideoRig: перехват ввода включён, кадр " + Screen.width + "x" + Screen.height);
        }

        private void Detach()
        {
            if (module != null)
                module.inputOverride = savedOverride;

            if (pointerInput != null)
                UnityEngine.Object.Destroy(pointerInput);

            InputSource.EndScript();
        }

        private IEnumerator Do(ShootAction action)
        {
            if (!Ready(action))
                yield break;

            switch (action.@do)
            {
                case "wait":
                    if (action.seconds <= 0)
                    {
                        Fail("действие wait без длительности");
                        yield break;
                    }

                    yield return new WaitForSeconds(action.seconds);
                    break;

                case "pointer":
                    if (!HasTarget(action))
                    {
                        Fail("действие pointer без цели");
                        yield break;
                    }

                    yield return MovePointer(action);
                    break;

                case "click":
                    if (HasTarget(action))
                        yield return MovePointer(action);

                    if (failure != null)
                        yield break;

                    // Цель могла сдвинуться, пока к ней вели указатель (живой враг ходит) — доводим
                    // указатель по её текущему месту, иначе нажатие ушло бы в пустую землю рядом.
                    if (HasTarget(action))
                    {
                        Vector3 point;

                        if (!TryTarget(action, out point))
                            yield break;

                        InputSource.SetPointer(point);
                    }

                    // Пока к цели вели указатель, персонажа могли убить либо снять с карты: условие шага
                    // спрашиваем заново по той же причине, по какой заново берём место цели.
                    if (!Ready(action))
                        yield break;

                    yield return Click();
                    break;

                case "walk_dir":
                    yield return WalkDirection(action);
                    break;

                default:
                    Fail("неизвестное действие сценария: " + action.@do);
                    break;
            }
        }

        private IEnumerator MovePointer(ShootAction action)
        {
            Vector3 to;

            if (!TryTarget(action, out to))
                yield break;

            Vector3 from = InputSource.MousePosition;
            float seconds = action.seconds > 0 ? action.seconds : POINTER_SECONDS;
            float passed = 0;

            // Указатель ведём, а не переставляем: в кадре зритель должен видеть, куда идёт рука.
            while (passed < seconds)
            {
                passed += Time.deltaTime;
                InputSource.SetPointer(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(passed / seconds))));
                yield return null;
            }

            InputSource.SetPointer(to);
            yield return null;
        }

        /// <summary>
        /// Нажатие и отпускание. Каждое состояние держим кадр: игра читает ввод раз в кадр, и нажатие
        /// короче кадра не увидел бы ни игровой код, ни интерфейс.
        /// </summary>
        private IEnumerator Click()
        {
            InputSource.PressMouse();
            yield return null;
            yield return null;

            InputSource.ReleaseMouse();
            yield return null;
            yield return null;
        }

        private IEnumerator WalkDirection(ShootAction action)
        {
            Vector2 direction = new Vector2(action.x, action.y);

            if (direction.sqrMagnitude < 0.0001f || action.seconds <= 0)
            {
                Fail("действие walk_dir без направления либо без длительности");
                yield break;
            }

            direction.Normalize();
            InputSource.SetAxis(direction.x, direction.y);

            yield return new WaitForSeconds(action.seconds);

            InputSource.SetAxis(0, 0);
            yield return null;
        }

        /// <summary>
        /// Условие, при котором шаг осмыслен. Спрашивается ПЕРЕД шагом и у всех троих участников:
        /// окружение — мир не на паузе загрузки; действующий — персонаж в мире и способен на то, чего
        /// шаг от него требует; цель — её разбирает <see cref="TryTarget"/> уже по месту.
        ///
        /// Действующего пропускают чаще прочих: отказ, собранный по одной цели, звучит осмысленно, а
        /// причина лежит у персонажа — и разбор уходит к цели. Снятое на невыполненном условии хуже
        /// неснятого: неподвижное тело в кадре легко принять за годный фрагмент.
        /// </summary>
        private bool Ready(ShootAction action)
        {
            if (LoadingScreen.IsShown)
            {
                Fail("мир на паузе: поднята панель загрузки — в кадр попала бы заставка");
                return false;
            }

            PlayerModel player = PlayerController.Player;

            if (player == null)
            {
                Fail("своего персонажа нет в мире — ни целиться, ни действовать некому");
                return false;
            }

            if (player.action == ConnectController.ACTION_REMOVE)
            {
                Fail("персонаж снят с карты — его команды до сервера уже не идут");
                return false;
            }

            if (Camera.main == null)
            {
                Fail("камеры мира нет — точку кадра по миру не посчитать");
                return false;
            }

            if (!ActsOnWorld(action))
                return true;

            if (!Alive(player))
            {
                Fail("персонаж не способен действовать: запас здоровья "
                    + (player.hp == null ? "неизвестен" : player.hp.Value.ToString())
                    + ", действие «" + player.action + "»");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Требует ли шаг ДЕЙСТВИЯ персонажа по миру — движения, выбора цели, удара — либо лишь ведёт
        /// указатель и трогает интерфейс. Мёртвый персонаж окно откроет, а с места не сойдёт и не
        /// ударит: жизни спрашиваем там, где без неё шаг бессмыслен, иначе отказ назвал бы не ту причину.
        ///
        /// У нажатия вид решает ЦЕЛЬ, а не само нажатие: имя объекта интерфейса — интерфейс; сущность,
        /// клетка карты и слот панели — мир. У точки кадра вид заранее не объявлен — спрашиваем то же,
        /// что спросит разбор нажатия в игре: лежит ли под точкой интерфейс.
        /// </summary>
        private bool ActsOnWorld(ShootAction action)
        {
            if (action.@do == "walk_dir")
                return true;

            if (action.@do != "click")
                return false;

            if (action.ui != null)
                return false;

            if (action.entity != null || action.map != null || action.bar > 0)
                return true;

            Vector3 point = action.screen != null && action.screen.Length == 2
                ? new Vector3(action.screen[0], action.screen[1], 0)
                : InputSource.MousePosition;

            return !OverUi(point);
        }

        /// <summary>Лежит ли под точкой кадра интерфейс — тем же путём, каким это спрашивает игра.</summary>
        private static bool OverUi(Vector3 point)
        {
            if (EventSystem.current == null)
                return false;

            PointerEventData pointer = new PointerEventData(EventSystem.current) { position = point };
            List<RaycastResult> hits = new List<RaycastResult>();

            EventSystem.current.RaycastAll(pointer, hits);

            return hits.Count > 0;
        }

        /// <summary>
        /// Живо ли существо — тем же признаком, каким живого выбирает клик по врагу: запас здоровья и
        /// не «мёртвая» поза. У выбранного через общий класс объекта запаса нет вовсе — такой не жив.
        /// </summary>
        private static bool Alive(EnemyModel entity)
        {
            return entity.hp != null && entity.hp.Value > 0 && entity.action != "dead";
        }

        private bool HasTarget(ShootAction action)
        {
            return action.screen != null || action.ui != null || action.bar > 0 || action.map != null || action.entity != null;
        }

        /// <summary>
        /// Куда на кадре смотрит указатель. Цель задаётся ровно одним полем: несколько разом означают,
        /// что автор сценария сам не знает, куда целится.
        /// </summary>
        private bool TryTarget(ShootAction action, out Vector3 point)
        {
            point = Vector3.zero;

            int declared = (action.screen != null ? 1 : 0) + (action.ui != null ? 1 : 0)
                + (action.bar > 0 ? 1 : 0) + (action.map != null ? 1 : 0) + (action.entity != null ? 1 : 0);

            if (declared != 1)
            {
                Fail("у действия " + action.@do + " объявлено целей: " + declared + ", а должна быть ровно одна");
                return false;
            }

            if (action.screen != null)
            {
                if (action.screen.Length != 2)
                {
                    Fail("цель screen задаётся парой чисел");
                    return false;
                }

                point = new Vector3(action.screen[0], action.screen[1], 0);
            }
            else if (action.ui != null && !TryUi(action.ui, out point))
                return false;
            else if (action.bar > 0 && !TryBar(action.bar, out point))
                return false;
            else if (action.map != null && !TryMap(action.map, out point))
                return false;
            else if (action.entity != null && !TryEntity(action.entity, out point))
                return false;

            if (!OnScreen(point))
            {
                Fail("цель действия " + action.@do + " лежит вне кадра: " + point);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Объект интерфейса ищем по имени, а при совпадении имён — по пути в иерархии: имена вроде
        /// «Close» носят кнопки всех окон разом, и одно имя на них не указывает ни на одну.
        /// </summary>
        private bool TryUi(string name, out Vector3 point)
        {
            point = Vector3.zero;
            RectTransform found = null;
            bool byPath = name.Contains("/");

            foreach (RectTransform rect in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Exclude))
            {
                if (byPath ? Path(rect.transform) != name : rect.name != name)
                    continue;

                if (found != null)
                {
                    Fail("объектов интерфейса " + name + " на экране несколько — целиться не во что");
                    return false;
                }

                found = rect;
            }

            if (found == null)
            {
                Fail("на экране нет объекта интерфейса " + name);
                return false;
            }

            return TryRect(found, out point);
        }

        private static string Path(Transform target)
        {
            string path = target.name;

            for (Transform parent = target.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;

            return path;
        }

        private bool TryBar(int num, out Vector3 point)
        {
            point = Vector3.zero;

            if (MainController.Instance == null)
            {
                Fail("интерфейса игры на экране нет — панели быстрых действий не найти");
                return false;
            }

            foreach (ActionBar bar in MainController.Instance.ActionBars)
            {
                if (bar == null || bar.num != num)
                    continue;

                return TryRect((RectTransform)bar.transform, out point);
            }

            Fail("на панели быстрых действий нет слота " + num);
            return false;
        }

        private bool TryRect(RectTransform rect, out Vector3 point)
        {
            point = Vector3.zero;
            Canvas canvas = rect.GetComponentInParent<Canvas>();

            if (canvas == null)
            {
                Fail("объект интерфейса " + rect.name + " лежит вне холста");
                return false;
            }

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rect.TransformPoint(rect.rect.center));
            point = new Vector3(screen.x, screen.y, 0);
            return true;
        }

        private bool TryMap(float[] cell, out Vector3 point)
        {
            point = Vector3.zero;

            if (cell.Length != 2)
            {
                Fail("цель map задаётся парой чисел");
                return false;
            }

            // Сущности стоят на чистой позиции карты: у карты, на которой игрок, смещение нулевое, и
            // клетка карты совпадает с мировой точкой. Глубину берём у игрока — камера смотрит на его слой.
            Vector3 world = new Vector3(cell[0], cell[1], PlayerController.Player.transform.position.z);
            point = Camera.main.WorldToScreenPoint(world);
            point.z = 0;
            return true;
        }

        private bool TryEntity(string key, out Vector3 point)
        {
            point = Vector3.zero;
            EntityModel found = null;

            if (key == "nearest_enemy")
            {
                float nearest = float.MaxValue;
                Vector3 from = PlayerController.Player.transform.position;

                foreach (EnemyModel enemy in UnityEngine.Object.FindObjectsByType<EnemyModel>(FindObjectsInactive.Exclude))
                {
                    // Игроки — тот же класс, что и существа: свой попал бы в поиск нулевым расстоянием,
                    // чужой сделал бы сценарий зависимым от того, кто ещё в мире.
                    if (enemy is PlayerModel)
                        continue;

                    if (!Alive(enemy))
                        continue;

                    // Кликнуть можно только по тому, что видно: враг за краем кадра указателем недостижим,
                    // и ближайшим по карте он оказывается сплошь и рядом — существа стоят по всей карте,
                    // а камера держит лишь клетки вокруг игрока.
                    if (!OnScreen(ScreenOf(enemy)))
                        continue;

                    float distance = (enemy.transform.position - from).sqrMagnitude;

                    if (distance >= nearest)
                        continue;

                    nearest = distance;
                    found = enemy;
                }

                if (found == null)
                {
                    Fail("в кадре нет живого врага — целиться не в кого");
                    return false;
                }
            }
            else
            {
                foreach (EntityModel entity in UnityEngine.Object.FindObjectsByType<EntityModel>(FindObjectsInactive.Exclude))
                {
                    if (entity.key != key)
                        continue;

                    found = entity;
                    break;
                }

                if (found == null)
                {
                    Fail("в мире нет сущности с ключом " + key);
                    return false;
                }
            }

            point = ScreenOf(found);
            return true;
        }

        /// <summary>
        /// Куда указателю целиться, чтобы попасть по сущности: середина её видимых границ. Позиция корня
        /// у сущностей на ногах, и целясь в неё указатель попадал бы в землю под телом.
        /// </summary>
        private static Vector3 ScreenOf(EntityModel entity)
        {
            Bounds bounds;
            Vector3 world = entity.TryGetVisualBounds(out bounds) ? bounds.center : entity.transform.position;

            Vector3 point = Camera.main.WorldToScreenPoint(world);
            point.z = 0;
            return point;
        }

        private static bool OnScreen(Vector3 point)
        {
            return point.x >= 0 && point.y >= 0 && point.x <= UnityEngine.Screen.width && point.y <= UnityEngine.Screen.height;
        }

        private void Fail(string message)
        {
            failure = message;
            Debug.LogError("VideoRig: " + message);
        }

        private void Finish()
        {
            recorder.End();
            Detach();

            Status = failure == null
                ? "готово, снято сцен: " + Files.Count
                : "отказ: " + failure + " (снято до отказа: " + Files.Count + ")";

            Debug.Log("VideoRig: " + Status);

            UnityEngine.Object.Destroy(host);
        }
    }
}
