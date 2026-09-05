using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mmogick
{
    /// <summary>
	/// Класс настройки зоны видимости вокруг игрока
	/// </summary>

    // этот скрипт будет работать даже без запуска игры в редакторе unity (онлайном показывать видимость игрока)
    [ExecuteInEditMode]
    public class CameraController : MonoBehaviour
    {
        private Dictionary<int, MapSide> last_sides;
        private float last_view;

        private float minX;
        private float minY;
        private float maxX;
        private float maxY;

        // LateUpdate, а не Update: камера обязана обновляться ПОСЛЕ всех, кто двигает её цель. Движение существ
        // идёт в корутине по кадрам отрисовки, а она возобновляется после Update — из Update камера видела бы
        // позицию игрока на шаг устаревшей, и это отставание дрожало бы вместе с длительностью кадра
        // (игрок «зубрился» относительно неподвижного фона). Все прочие следящие за целью скрипты — полоска
        // жизни, оружие в руке, метки на земле — по той же причине живут в LateUpdate.
        private void LateUpdate()
        {
            if (PlayerController.Player != null)
		    {
                /// <summary>
                /// зона видимости вокруг игрока
                /// </summary>
                float targetRation = 1;
                float height;

                // Экран показывает ПОЛОВИНУ радиуса жизни, а не весь его: за радиусом жизни механики не
                // идут, и мини-карта, построенная по нему (MinimapController), обязана быть заметно шире
                // экрана — иначе она повторяет то, что и так видно. Прежде экран строился по всему радиусу,
                // и при обычном соотношении сторон его ширина почти равнялась ему: мини-карта дублировала
                // экран. Делится именно радиус жизни, а не своя настройка обзора: правило «мини-карта вдвое
                // шире экрана» держится тогда при любом значении, которое игрок выставит.
                float view = PlayerController.Player.lifeRadius / 2f;

                if (Camera.main.aspect >= targetRation)
                {
                    height = (view - 0.5f) / 2;
                }
                else
                {
                    float defferenceSize = targetRation / Camera.main.aspect;
                    height = (view - 0.5f) / 2 * defferenceSize;
                }

                if(Camera.main.orthographicSize != height)
                {
                    Camera.main.orthographicSize = height;
                }

                // Кламп к границам обзора — только когда номер карты игрока СОГЛАСОВАН со списком соседей:
                // текущая карта обязана присутствовать в getSides() со смещением (0,0). При переходе пакет sides
                // обновляется на кадр раньше Player.map, и в этот кадр они противоречивы (sides уже про новую карту,
                // Player.map ещё про старую) — UpdateView посчитал бы границы по чужой карте (реального соседа приняв
                // за текущую), а кламп дёрнул бы камеру по урезанной границе. Пока рассогласовано — идём в else (камера
                // плавно следует за игроком без клампа); согласуется — пересчёт по свежему sides (last_sides != ссылка).
                Dictionary<int, MapSide> sides = PlayerController.getSides();
                if (PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.getMaps().Count > 0 && sides.Count == PlayerController.getMaps().Count && sides.Keys.SequenceEqual(PlayerController.getMaps().Keys) && sides.TryGetValue(PlayerController.Player.map, out MapSide current) && current.x == 0 && current.y == 0)
                {
                    if (last_sides != sides || last_view != view)
                    {
                        UpdateView();
                        last_view = view;
                    }

                    Camera.main.transform.position = new Vector3(Mathf.Clamp(PlayerController.Player.transform.position.x, minX, maxX), Mathf.Clamp(PlayerController.Player.transform.position.y, minY, maxY), Camera.main.transform.position.z);
                }
                else
                    Camera.main.transform.position = new Vector3(PlayerController.Player.transform.position.x, PlayerController.Player.transform.position.y, Camera.main.transform.position.z);
               }
        }

        private void UpdateView()
        {
            Dictionary<int, MapDecode> maps = PlayerController.getMaps();
            float width = Camera.main.orthographicSize * Camera.main.aspect;

            minX = 0 + width;
            minY = maps[PlayerController.Player.map].height * -1 + Camera.main.orthographicSize + 1;

            maxX = maps[PlayerController.Player.map].width - width;
            maxY = 1 - Camera.main.orthographicSize;

            last_sides = PlayerController.getSides();

            // если НЕ только текущая карта
            if (last_sides.Count > 1)
            {
                Debug.Log("Камера: ищем соседнии области карты " + PlayerController.Player.map + " для захвата камеры ");
                foreach (KeyValuePair<int, MapSide> side in last_sides)
                {
                    // текущая карта нас не интересует
                    if (side.Key == PlayerController.Player.map)
                        continue;

                    // еще не все карты ббыли загружены
                    if (!maps.ContainsKey(side.Key))
                    {
                        Camera.main.transform.position = new Vector3(PlayerController.Player.transform.position.x, PlayerController.Player.transform.position.y, Camera.main.transform.position.z);
                        return;
                    }

                    if (side.Value.y == 0 || (side.Value.x < 0 || maps[side.Key].width + side.Value.x > maps[PlayerController.Player.map].width))
                    {
                        // если справа или слева на одной линии
                        if (side.Value.y == 0)
                        {
                            if (side.Value.x > 0)
                                maxX += maps[side.Key].width;
                            if (side.Value.x < 0)
                                minX -= maps[side.Key].width;
                        }
                        // если снизу или сверху но левее или праваее
                        else
                        {
                            if (side.Value.x > 0)
                                maxX += side.Value.x + maps[side.Key].width - maps[PlayerController.Player.map].width;
                            if (side.Value.x < 0)
                            {
                                minX -= side.Value.x * -1;
                                maxX = Math.Max(maxX, maps[side.Key].width + side.Value.x);
                            }
                        }
                    }

                    if (side.Value.x == 0 || (side.Value.y > 0 || maps[side.Key].height + side.Value.y * -1 > maps[PlayerController.Player.map].height))
                    {
                        // если сверху или снизу
                        if (side.Value.x == 0)
                        {
                            // если карта находиться выше текущей
                            if (side.Value.y > 0)
                                maxY += maps[side.Key].height;
                            if (side.Value.y < 0)
                                minY -= maps[side.Key].height;
                        }
                        else
                        {
                            if (side.Value.y > 0)
                            {
                                maxY += side.Value.y;

                                // может быть что и карта находится сбоку ее нижняя точка будет больше нашей карты
                                minY = Math.Min(minY, maps[side.Key].height - side.Value.y);
                            }
                            if (side.Value.y < 0)
                                minY -= maps[side.Key].height + side.Value.y * -1 - maps[PlayerController.Player.map].height;
                        }
                    }
                }
            }

            // Границы посчитаны по ЛОГИЧЕСКИМ клеткам карты, а игрок видит полотно тайлов, лежащее относительно
            // них со смещением MapController.GRID_DRAW_OFFSET: упираться камера обязана в край НАРИСОВАННОГО.
            // Смещение общее всем картам, потому объединённая с соседями область смещена им же — переводим
            // все четыре границы разом, после расширения на соседей.
            minX += MapController.GRID_DRAW_OFFSET.x;
            maxX += MapController.GRID_DRAW_OFFSET.x;

            minY += MapController.GRID_DRAW_OFFSET.y;
            maxY += MapController.GRID_DRAW_OFFSET.y;

            // Область обзора не уже/не ниже карты — диапазон вырожден (min > max), а Mathf.Clamp при min > max
            // отдаёт не середину, а одну из границ, смотря с какой стороны от min стоит игрок. Середина такого
            // диапазона тождественно равна центру области, потому сводим обе границы к ней: камера встаёт по центру.
            if (minX > maxX)
                minX = maxX = (minX + maxX) / 2;

            if (minY > maxY)
                minY = maxY = (minY + maxY) / 2;
        }
    }
}
