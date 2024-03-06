using System;
using System.Collections.Generic;
using UnityEngine;
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
        private GameObject SettingPrefabCheckbox;

        /// <summary>
        /// префабы блоков настроек - скроллинг
        /// </summary>
        [SerializeField]
        private GameObject SettingPrefabScroll;        
        
        /// <summary>
        /// префабы блоков настроек - выпадающий список
        /// </summary>
        [SerializeField]
        private GameObject SettingPrefabDropdown;

        /// <summary>
        /// настрйоки ключ - значение
        /// </summary>
        private Dictionary<string, string> _settings = new Dictionary<string, string>();

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            if (key == player_key && ((PlayerRecive)recive).components != null)
            {
                Dictionary<string, Setting> settings = ((PlayerRecive)recive).components.settings;
                if (settings != null)
                {
                    _settings.Clear();
                    foreach (Transform child in SettingArea.transform)
                    {
                        Destroy(child.gameObject);
                    }

                    GameObject prefab;
                    foreach (var setting in settings)
                    {
                        switch (setting.Value.type)
                        {
                            case "checkbox":
                                prefab = Instantiate(SettingPrefabCheckbox) as GameObject;
                                Toggle toggle = prefab.GetComponentInChildren<Toggle>();

                                toggle.isOn = (float.Parse(setting.Value.value) != 0 ? true : false);
                                toggle.onValueChanged.AddListener(delegate { CheckboxOnChange(setting.Key, toggle); });
                            break;                            
                            case "slider":
                                prefab = Instantiate(SettingPrefabScroll) as GameObject;

                                Slider slider = prefab.GetComponentInChildren<Slider>();
                                Text text = prefab.transform.Find("Value").GetComponent<Text>();

                                if (setting.Value.min != null)
                                    slider.minValue = (float)setting.Value.min;
                                if (setting.Value.max != null)
                                    slider.maxValue = (float)setting.Value.max;

                                slider.value = float.Parse(setting.Value.value);

                                if(text!=null)
                                    text.text = setting.Value.value;

                                slider.onValueChanged.AddListener(delegate { ScrollOnChange(setting.Key, slider, text); });
                            break;                           
                            case "dropdown":
                                prefab = Instantiate(SettingPrefabDropdown) as GameObject;
                                Dropdown dropdown = prefab.GetComponentInChildren<Dropdown>();

                                List<string> list1 = new List<string>(setting.Value.values.Values);
                                string[] list2 = new List<string>(setting.Value.values.Keys).ToArray();
                               
                                dropdown.ClearOptions();
                                dropdown.AddOptions(list1);
                                dropdown.value = Array.IndexOf(list2, setting.Value.value);
                                dropdown.onValueChanged.AddListener(delegate { DropdownOnChange(setting.Key, dropdown, list2); });
                            break;
                            default:
                                Error("С сервера пришла настройка с остутвующим в клиенте типом " + setting.Value.type);
                            return null;
                        }

                        prefab.name = setting.Key;
                        if (prefab.transform.Find("Title") != null && prefab.transform.Find("Title").GetComponent<Text>() != null)
                            prefab.transform.Find("Title").GetComponent<Text>().text = setting.Value.title;

                        prefab.transform.SetParent(SettingArea.transform);
                        _settings[setting.Key] = setting.Value.value;
                    }
                }
            }

            return base.UpdateObject(map_id, key, recive, type);
        }

        private void ScrollOnChange(string key, Slider slider, Text text)
        {
            if(text!=null)
                text.text = slider.value.ToString();

            _settings[key] = slider.value.ToString();
        }       
        
        private void CheckboxOnChange(string key, Toggle obj)
        {
            _settings[key] = (obj.isOn?"1":"0");
        }        
        
        private void DropdownOnChange(string key, Dropdown obj, string[] list)
        {
            _settings[key] = list[obj.value];
        }

        public void Save()
        {
            SettingsResponse response = new SettingsResponse();
            response.settings = _settings;
            response.Send();
        }
    }
}