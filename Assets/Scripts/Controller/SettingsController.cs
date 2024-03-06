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
	/// Класс для обновления Меню настрое игрока
	/// </summary>
    abstract public class SettingsController : UpdateController
    {
        /// <summary>
        /// поле для генерации объектов настроек
        /// </summary>
        [SerializeField]
        private GameObject SettingArea;

        /// <summary>
        /// префабы блоков настроек - чекбокс
        /// </summary>
        [SerializeField]
        private GameObject SettingCheckbox;

        /// <summary>
        /// префабы блоков настроек - скроллинг
        /// </summary>
        [SerializeField]
        private GameObject ScrollCheckbox;        
        
        /// <summary>
        /// префабы блоков настроек - выпадающий список
        /// </summary>
        [SerializeField]
        private GameObject ScrollDropdown;

        /// <summary>
        /// настрйоки ключ - значение
        /// </summary>
        private Dictionary<string, string> _settings = new Dictionary<string, string>();

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            if (key == player_key)
            {
                if (((PlayerRecive)recive).components != null && ((PlayerRecive)recive).components.settings!=null)
                {
                    _settings.Clear();
                    foreach (Transform child in SettingArea.transform)
                    {
                        Destroy(child.gameObject);
                    }
                    GameObject prefab;
                    foreach (var setting in ((PlayerRecive)recive).components.settings)
                    {
                        switch (setting.Value.type)
                        {
                            case "checkbox":
                                prefab = Instantiate(SettingCheckbox) as GameObject;
                            break;                            
                            case "scroll":
                                prefab = Instantiate(ScrollCheckbox) as GameObject;
                            break;                           
                            case "dropdown":
                                prefab = Instantiate(ScrollDropdown) as GameObject;
                            break;
                            default:
                                Error("С сервера пришла настройка с остутвующим в клиенте типом " + setting.Value.type);
                            return null;
                        }

                        prefab.name = setting.Key;
                        if (prefab.transform.Find("Title") != null && prefab.transform.Find("Title").GetComponent<Text>() != null)
                            prefab.transform.Find("Title").GetComponent<Text>().text = setting.Value.title;

                        prefab.transform.SetParent(SettingArea.transform);
                        _settings.Add(setting.Key, setting.Value.value);
                    }
                       
                    Debug.LogError(_settings);
                }
            }

            return base.UpdateObject(map_id, key, recive, type);
        }


        public void Save()
        {
            SettingsResponse response = new SettingsResponse();
            response.settings = _settings;
            response.Send();
        }
    }
}