using UnityEngine;
using MyFantasy;
using UnityEngine.UI;
using System;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Linq;

namespace MyFantasy
{
    /// <summary>
	/// Класс настройки зоны видимости вокруг игрока
	/// </summary>

    // этот скрипт будет работать даже без запуска игры в редакторе unity (онлайном показывать видимость игрока)
    [ExecuteInEditMode]
    public class CameraController : MonoBehaviour
    {
        private Dictionary<int, Point> last_sides;
        private int last_lifeRadius;

        private float minX;
        private float minY;
        private float maxX;
        private float maxY;

        private void Update()
        {
            if (PlayerController.Player != null)
		    {
                /// <summary>
                /// зона видимости вокруг игрока
                /// </summary>
                float targetRation = 1;
                float height;

                if (Camera.main.aspect >= targetRation)
                { 
                    height = (PlayerController.Player.lifeRadius - 0.5f) / 2;
                }
                else
                {
                    float defferenceSize = targetRation / Camera.main.aspect;
                    height = (PlayerController.Player.lifeRadius - 0.5f) / 2 * defferenceSize;
                }

                if(Camera.main.orthographicSize != height)
                {
                    Camera.main.orthographicSize = height;
                }

                // если все карты скачены и мы не удаляемся с карты
                if (PlayerController.Player.action != PlayerController.ACTION_REMOVE && PlayerController.getMaps().Count > 0 && PlayerController.getSides().Count == PlayerController.getMaps().Count && PlayerController.getSides().Keys.SequenceEqual(PlayerController.getMaps().Keys))
                {
                    // контроли видимости за край карты 
                    if (last_sides != PlayerController.getSides() || last_lifeRadius != PlayerController.Player.lifeRadius)
                    {
                        UpdateView();
                        last_lifeRadius = PlayerController.Player.lifeRadius;
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
            minY = maps[PlayerController.Player.map_id].height * -1 + Camera.main.orthographicSize + 1;

            maxX = maps[PlayerController.Player.map_id].width - width;
            maxY = 1 - Camera.main.orthographicSize;

            last_sides = PlayerController.getSides();

            // если НЕ только текущая карта
            if (last_sides.Count > 1)
            {
                Debug.Log("Камера: ищем соседнии области карты " + PlayerController.Player.map_id + " для захвата камеры ");
                foreach (KeyValuePair<int, Point> side in last_sides)
                {
                    // текущая карта нас не интересует
                    if (side.Key == PlayerController.Player.map_id)
                        continue;

                    // еще не все карты ббыли загружены
                    if (!maps.ContainsKey(side.Key))
                    {
                        Camera.main.transform.position = new Vector3(PlayerController.Player.transform.position.x, PlayerController.Player.transform.position.y, Camera.main.transform.position.z);
                        return;
                    }

                    if (side.Value.y == 0 || (side.Value.x < 0 || maps[side.Key].width + side.Value.x > maps[PlayerController.Player.map_id].width))
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
                                maxX += side.Value.x + maps[side.Key].width - maps[PlayerController.Player.map_id].width;
                            if (side.Value.x < 0)
                            {
                                minX -= side.Value.x * -1;
                                maxX = Math.Max(maxX, maps[side.Key].width + side.Value.x);
                            }
                        }
                    }

                    if (side.Value.x == 0 || (side.Value.y > 0 || maps[side.Key].height + side.Value.y * -1 > maps[PlayerController.Player.map_id].height))
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
                                minY -= maps[side.Key].height + side.Value.y * -1 - maps[PlayerController.Player.map_id].height;
                        }
                    }
                }
            }
        }
    }
}
