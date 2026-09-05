using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Mmogick.VideoRig
{
    /// <summary>
    /// Исполнитель сценария съёмки: ведёт указатель, нажимает, водит персонажа и режет запись по сценам —
    /// границы фрагмента задаёт сама сцена (<see cref="ShootScene"/>). Живёт только в редакторе и только
    /// пока идёт прогон.
    ///
    /// Шаги идут корутиной в такт кадрам игры — ввод игра читает раз в кадр, и нажатие короче кадра не
    /// увидел бы никто. Крутит корутину объект-носитель <see cref="ScenarioHost"/>: собственным
    /// компонентом сцены исполнитель быть не может, он из редакторной сборки.
    ///
    /// Ввод отдаётся сценарию целиком (<see cref="InputSource"/> плюс <see cref="ScenarioPointerInput"/>
    /// у EventSystem): движения руки человека в запись попадать не должны. Отказ на любом шаге —
    /// немедленная остановка со снятым перехватом, сносом уже снятых фрагментов и ошибкой игровым каналом
    /// (<see cref="ConnectController.Error"/>): она уводит на экран входа и показывает там текст, как
    /// показывает любую другую ошибку игры. Снятое «наполовину» хуже неснятого — его легко принять за
    /// годный фрагмент, потому материал отказавшего прогона до сборщика ролика не доходит. Оборванный
    /// прогон (игру остановили посреди съёмки) идёт тем же порядком — <see cref="Abort"/>.
    /// </summary>
    public sealed class ScenarioRunner
    {
        /// <summary>Сколько ведём указатель, когда сцена не сказала иначе.</summary>
        private const float POINTER_SECONDS = 0.5f;

        /// <summary>
        /// Подвес между началом записи и первым её действием — и между последним действием сцены и концом
        /// записи: фрагмент не начинается и не кончается в движении.
        /// </summary>
        private const float SCENE_PAD = 0.4f;

        /// <summary>Пауза после остановки записи — пакет дописывает файл.</summary>
        private const float FLUSH = 1f;

        /// <summary>Во сколько раз указатель в кадре крупнее системного: 32 пикселя на кадре 1080p мелки для ролика.</summary>
        private const float POINTER_SCALE = 1.5f;

        /// <summary>
        /// Сколько рука держится в кадре после действия, которым она пользовалась. На записи в 30 кадров/с
        /// это около десятка кадров: зритель успевает увидеть руку на месте нажатия, а дальше кадр
        /// остаётся чистым. Убранная в тот же кадр рука на записи читается сбоем, а не действием.
        /// </summary>
        private const float POINTER_HOLD = 0.35f;

        /// <summary>Сколько ждём мир после запуска: вход в игру сетевой, карта грузится не мгновенно.</summary>
        private const float WORLD_TIMEOUT = 90f;

        /// <summary>
        /// Сколько ждём готовности мира после перехода, когда шаг не сказал иначе. Меньше входного потолка:
        /// карта у сервера к этому времени уже поднята, ждём лишь ответ на переход и графику вокруг игрока.
        /// </summary>
        private const float MAP_TIMEOUT = 30f;

        /// <summary>
        /// Окно, в которое шаг ожидания перехода ждёт САМОГО перехода — подъёма панели загрузки либо смены
        /// карты под персонажем. Сервер отвечает на дверь не в тот же кадр, и без этого окна шаг прошёл бы
        /// мгновенно по ещё готовому миру прежней карты, а панель поднялась бы уже на следующем шаге.
        /// </summary>
        private const float MAP_GRACE = 1.5f;

        /// <summary>Ход прогона — его опрашивает снаружи тот, кто прогон запустил.</summary>
        public static string Status { get; private set; } = "не запускался";

        /// <summary>Снятые фрагменты последнего прогона.</summary>
        public static List<string> Files { get; private set; } = new List<string>();

        /// <summary>
        /// Идущий прогон. Обрыв съёмки — остановка игры человеком, пересборка кода, закрытие редактора —
        /// убивает корутину молча: до <see cref="Finish"/> прогон не доезжает, и снятое остаётся лежать у
        /// сборщика ролика. Снимает его <see cref="Abort"/>, а дотянуться до прогона ему нечем, кроме
        /// статики — зовёт его редакторный хук, о самом прогоне не знающий.
        /// </summary>
        private static ScenarioRunner current;

        private ShootScenario scenario;

        private SceneRecorder recorder;

        private ScenarioPointerInput pointerInput;

        private BaseInputModule module;

        private BaseInput savedOverride;

        private string failure;

        /// <summary>
        /// Шаг, исполняемый сейчас: им отказ называет своё место в сценарии наравне с ходом прогона
        /// (<see cref="Status"/>). Ставится на каждом действии сцены и снимается на её начале — отказ до
        /// первого действия принадлежит сцене, а не последнему шагу предыдущей.
        /// </summary>
        private ShootAction step;

        /// <summary>
        /// Фрагмент, который пишется прямо сейчас. В <see cref="Files"/> он попадает только по закрытию
        /// сцены, а на диске лежит с начала записи — снос негодного материала берёт и его.
        /// </summary>
        private string fragment;

        private readonly GameObject host;

        internal ScenarioRunner(ShootScenario scenario, string outputDir, GameObject host)
        {
            this.scenario = scenario;
            this.host = host;

            recorder = new SceneRecorder(outputDir, scenario.width, scenario.height, scenario.fps);

            Files = new List<string>();
            Status = "запущен";
            current = this;
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

                fragment = null;
                step = null;

                if (!scene.actions.Exists(mark => mark.@do == ShootAction.RECORD))
                {
                    if (!TryBegin(scene))
                        break;

                    yield return new WaitForSeconds(SCENE_PAD);
                }

                foreach (ShootAction action in scene.actions)
                {
                    step = action;

                    if (action.@do == ShootAction.RECORD)
                    {
                        if (!TryBegin(scene))
                            break;

                        yield return new WaitForSeconds(SCENE_PAD);
                        continue;
                    }

                    yield return Do(action);

                    // Рука живёт ровно вокруг действия, которое ею пользуется: показывает её ведение
                    // указателя, а снимается она здесь — подержавшись POINTER_HOLD, чтобы нажатие
                    // прочиталось на записи. Пауза идёт только там, где рука была показана: у паузы и
                    // ходьбы указателя в кадре нет, и длительности сценария она им не сдвигает. Место
                    // общее для всех действий и потому покрывает и отказ посреди ведения — иначе рука
                    // осталась бы в кадре, а снять её после конца прогона уже нечем.
                    if (InputSource.PointerShown)
                    {
                        yield return new WaitForSeconds(POINTER_HOLD);
                        InputSource.HidePointer();
                    }

                    if (failure != null)
                        break;
                }

                // Отказ случился до маркера — записи не начиналось, и закрывать нечего.
                if (fragment == null)
                    break;

                yield return new WaitForSeconds(SCENE_PAD);

                recorder.End();
                Files.Add(fragment);
                fragment = null;

                yield return new WaitForSeconds(FLUSH);
            }

            Finish();
        }

        /// <summary>
        /// Начать фрагмент сцены. Отказ пакета записи прекращает прогон: снимать дальше нечем.
        /// </summary>
        private bool TryBegin(ShootScene scene)
        {
            try
            {
                fragment = recorder.Begin(scene.id);
            }
            catch (Exception ex)
            {
                // Наружу уходит только текст, а разбираться придётся по месту отказа: без стека
                // причина видна лишь по сообщению.
                Debug.LogException(ex);
                Fail(ex.Message);
                return false;
            }

            return true;
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
            module = EventSystem.current != null ? EventSystem.current.currentInputModule : null;

            if (module == null)
            {
                Fail("у сцены нет EventSystem с модулем ввода — нажатия сценария не дошли бы до интерфейса");
                return;
            }

            // Указатель ждёт в середине кадра: оттуда идёт первое ведение, и путь руки к цели виден
            // зрителю целиком. Самой руки в кадре пока нет — её показывает ведение.
            InputSource.BeginScript(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));

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
            // Ожидание перехода — единственный шаг, осмысленный при поднятой панели загрузки: её снятия он
            // и ждёт. Общее условие шага панель отбивает целиком, потому у этого шага своё — оно внутри.
            if (action.@do == "wait_map")
            {
                yield return WaitMap(action);
                yield break;
            }

            if (!Ready(action))
                yield break;

            switch (action.@do)
            {
                case "wait":
                    if (action.seconds <= 0)
                    {
                        Fail("не задана длительность");
                        yield break;
                    }

                    yield return new WaitForSeconds(action.seconds);
                    break;

                case "pointer":
                    if (!HasTarget(action))
                    {
                        Fail("цель не задана");
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
                    Fail("вид действия сценарию неизвестен");
                    break;
            }
        }

        private IEnumerator MovePointer(ShootAction action)
        {
            Vector3 to;

            if (!TryTarget(action, out to))
                yield break;

            // Рука появляется в кадре ровно у тех действий, что ею пользуются: ведение к цели и нажатие по
            // цели, которое с того же ведения начинается. Пауза, ходьба по направлению и нажатие без цели
            // идут без указателя — зритель смотрит игру, а не сеанс работы за компьютером. Снимает руку
            // прогон по концу действия (Play).
            if (!InputSource.ShowPointer(POINTER_SCALE))
            {
                Fail("не нашлась картинка указателя");
                yield break;
            }

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

        /// <summary>
        /// Дождаться, пока мир снова готов принимать действия после перехода дверью. Дверь уводит на карту,
        /// которой у клиента ещё нет: сервер поднимает панель загрузки на несколько секунд, и всякий
        /// следующий шаг сценария на ней отбивается (<see cref="Ready"/>) — без этого шага сам переход в
        /// кадр не снять.
        ///
        /// Ждём ГОТОВНОСТИ мира, а не факта смены карты: дверь срабатывает от шага НА её клетку, и переход
        /// случается посреди предыдущего действия — ходьбы. К началу этого шага карта бывает уже сменена, и
        /// условие «дождись смены» ждало бы второго перехода, которого сценарий не заказывал. Готовность
        /// спрашиваем тем же составом, что вход в сценарий (<see cref="WaitWorld"/>): панель снята,
        /// персонаж и камера на месте.
        ///
        /// Обратный случай — переход ещё не начался: сервер отвечает на дверь не в тот же кадр, готовым
        /// стоит мир ПРЕЖНЕЙ карты, и шаг прошёл бы мгновенно. От этого держится окно <see cref="MAP_GRACE"/>:
        /// в нём шаг ждёт самого перехода — панели загрузки либо смены карты под персонажем.
        ///
        /// Переход бесшовной границей открытого мира идёт тем же шагом: карта меняется и там, а панели
        /// просто не бывает — ждать её незачем.
        /// </summary>
        private IEnumerator WaitMap(ShootAction action)
        {
            PlayerModel player = PlayerController.Player;

            // Персонажа в мире нет — переход уже идёт: на смене карты его объект пересоздают, и между
            // старым и новым он отсутствует. Прежней карты у такого шага нет (null), признака перехода ждать
            // незачем — сразу ждём готовности.
            int? from = player?.map;
            float limit = action.seconds > 0 ? action.seconds : MAP_TIMEOUT;
            float deadline = Time.realtimeSinceStartup + limit;
            float grace = Time.realtimeSinceStartup + MAP_GRACE;

            while (from != null && Time.realtimeSinceStartup < grace && !LoadingScreen.IsShown
                && PlayerController.Player != null && PlayerController.Player.map == from)
                yield return null;

            // Признака перехода не появилось вовсе: персонаж не дошёл до клетки двери либо шаг стоит не за
            // тем действием. Молча пропустить нельзя — сцена снялась бы без того, ради чего её и снимают.
            if (from != null && !LoadingScreen.IsShown
                && PlayerController.Player != null && PlayerController.Player.map == from)
            {
                Fail("перехода не случилось за " + MAP_GRACE + " с: персонаж остался на карте " + from
                    + " — до клетки двери он не дошёл либо шаг стоит не за тем действием");
                yield break;
            }

            while (LoadingScreen.IsShown || PlayerController.Player == null || Camera.main == null)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Fail("мир не пришёл в готовность за " + limit + " с после перехода");
                    yield break;
                }

                yield return null;
            }

            Reattach();
        }

        /// <summary>
        /// Перевесить перехват ввода на модуль ТЕКУЩЕЙ сцены. Переход дверью грузит игровую сцену заново —
        /// у панели загрузки это отдельная ступень, — и EventSystem вместе с модулем ввода пересоздаётся:
        /// перехват, поставленный на прежний модуль, уходит с ним, а нажатия сценария по интерфейсу
        /// перестают доходить. Молча: нажатия по МИРУ идут не через модуль и работать продолжают, потому
        /// сцена снимается «почти правильно» — окно, которое должно было открыться, просто не появляется.
        ///
        /// Прежний модуль уничтожен вместе со сценой, снимать с него перехват нечем и незачем; ввод,
        /// который стоял на новом модуле до нас, запоминаем — его вернёт <see cref="Detach"/>.
        /// </summary>
        private void Reattach()
        {
            BaseInputModule fresh = EventSystem.current != null ? EventSystem.current.currentInputModule : null;

            if (fresh == null || fresh == module)
                return;

            module = fresh;
            savedOverride = fresh.inputOverride;
            pointerInput = fresh.gameObject.AddComponent<ScenarioPointerInput>();
            fresh.inputOverride = pointerInput;

            Debug.Log("VideoRig: перехват ввода перевешен на модуль новой сцены");
        }

        private IEnumerator WalkDirection(ShootAction action)
        {
            Vector2 direction = new Vector2(action.x, action.y);

            if (direction.sqrMagnitude < 0.0001f || action.seconds <= 0)
            {
                Fail("не задано направление либо длительность");
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
                Fail("объявлено целей: " + declared + ", а должна быть ровно одна");
                return false;
            }

            if (action.screen != null)
            {
                if (action.screen.Length != 2)
                {
                    Fail("точка кадра задаётся парой чисел");
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
                Fail("цель лежит вне кадра: " + point);
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
                    Fail("объектов интерфейса с таким именем на экране несколько — целиться не во что");
                    return false;
                }

                found = rect;
            }

            if (found == null)
            {
                Fail("такого объекта интерфейса на экране нет");
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

            Fail("такого слота на панели быстрых действий нет");
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
                Fail("клетка карты задаётся парой чисел");
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
                    Fail("сущности с таким ключом в мире нет");
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

        /// <summary>
        /// Отказ прогона. Текст называет МЕСТО в сценарии — ход прогона плюс шаг с его объявленной целью, —
        /// и лишь затем причину: по одной причине место не восстановить, а по одной экранной точке разбор
        /// сводится к гаданию, какая сцена и куда целилась. Состав одинаков у каждого отказа: разбор не
        /// должен зависеть от того, на какой проверке упало.
        /// </summary>
        private void Fail(string message)
        {
            string place = Status;

            if (step != null)
            {
                // Цель объявляется ровно одним полем (TryTarget), потому в место идёт первое непустое;
                // шаг без цели (пауза, ходьба по направлению) называется одним своим видом.
                string target = step.screen != null ? " screen [" + string.Join(",", step.screen) + "]"
                    : step.ui != null ? " ui " + step.ui
                    : step.bar > 0 ? " bar " + step.bar
                    : step.map != null ? " map [" + string.Join(",", step.map) + "]"
                    : step.entity != null ? " entity " + step.entity
                    : "";

                place += ", шаг " + step.@do + target;
            }

            failure = place + " — " + message;
            Debug.LogError("VideoRig: " + failure);
        }

        /// <summary>
        /// Снять фрагменты отказавшего прогона: материал с обрывом посреди действия негоден весь, а
        /// сборщик ролика берёт из каталога вывода всё, что там лежит, — оставленный файл уехал бы в ролик.
        /// Снимаются РОВНО свои фрагменты, поимённо: в том же каталоге лежат фрагменты прошлых прогонов, и
        /// маска по каталогу забрала бы их вместе со своими.
        ///
        /// Зовётся после остановки записи: поток файла пакет закрывает прямо в ней и файл отпускает, а
        /// удерживаемый им файл не снять.
        /// </summary>
        private void Discard()
        {
            // Фрагмент, который писался в момент отказа либо обрыва, в перечень снятых ещё не попал, а на
            // диске уже лежит — сборщику ролика он неотличим от годного.
            if (fragment != null)
            {
                Files.Add(fragment);
                fragment = null;
            }

            foreach (string file in Files)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch (Exception ex)
                {
                    // Фрагмент остался лежать у сборщика — это тяжелее самого отказа прогона: без этой
                    // строки брак ушёл бы в ролик молча.
                    Debug.LogException(ex);
                    failure += "; фрагмент " + file + " остался в каталоге вывода: " + ex.Message;
                }
            }

            Files = new List<string>();
        }

        private void Finish()
        {
            // Регистрацию снимает только СВОЙ прогон: запуск второго поверх недоигравшего первого
            // перевешивает её на себя, и чужое завершение оставило бы обрыв нового без сноса.
            if (current == this)
                current = null;

            recorder.End();
            Detach();

            int shot = Files.Count;

            if (failure != null)
                Discard();

            Status = failure == null
                ? "готово, снято сцен: " + shot
                : "отказ: " + failure + " (снятое удалено, фрагментов: " + shot + ")";

            Debug.Log("VideoRig: " + Status);

            UnityEngine.Object.Destroy(host);

            // Отказ прогона — ошибка игры: игровой канал копит текст, закрывает соединение и следующим
            // кадром уводит на экран входа, где текст и показывается. Ставится последним: после него
            // прогону делать нечего, а состояние, на котором он оборван, канал уже признал негодным.
            if (failure != null)
                ConnectController.Error("VideoRig: " + failure);
        }

        /// <summary>
        /// Снять оборванный прогон: игру остановили посреди съёмки — руками, пересборкой кода либо закрытием
        /// редактора. Корутина умирает вместе с игрой, <see cref="Finish"/> не отрабатывает, и фрагменты
        /// обрыва уехали бы к сборщику ролика наравне с годными.
        ///
        /// Запись останавливаем сами: поток файла пакет закрывает прямо в остановке, потому снимать
        /// фрагменты сразу за ней безопасно. Игровым каналом ошибку не поднимаем — игры, которой её
        /// показывать, уже нет.
        /// </summary>
        internal static void Abort()
        {
            if (current == null)
                return;

            ScenarioRunner run = current;
            current = null;

            run.recorder.End();

            int shot = Files.Count + (run.fragment != null ? 1 : 0);

            run.failure = "игру остановили посреди съёмки";
            run.Discard();

            Status = "обрыв: " + run.failure + " (снятое удалено, фрагментов: " + shot + ")";
            Debug.Log("VideoRig: " + Status);
        }
    }
}
