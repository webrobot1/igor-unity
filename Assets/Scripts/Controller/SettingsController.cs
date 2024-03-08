using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WebGLSupport;

namespace MyFantasy
{
    /// <summary>
	/// Класс для обновления Меню настрое игрока
	/// </summary>
    abstract public class SettingsController : UpdateController
    {
        /// <summary>
        /// наш джойстик
        /// </summary>
        [SerializeField]
        protected VariableJoystick joystick;

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
        private Dictionary<string, string> _types = new Dictionary<string, string>();       
        
        /// <summary>
        /// список выпадающих списокв
        /// </summary>
        private Dictionary<string, string[]> _lists = new Dictionary<string, string[]>();


        protected override void Awake()
        {
            base.Awake();

            if (joystick == null)
                Error("не указан джойстик");

#if UNITY_WEBGL && !UNITY_EDITOR
                 WebGLRotation.Rotation(1);
#else
            Screen.orientation = ScreenOrientation.LandscapeRight;
            Screen.autorotateToPortrait = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
#endif           
        }

        protected virtual void HandleData(NewRecive<PlayerRecive, EnemyRecive, ObjectRecive> recive)
        {
            if (recive.settings != null)
            {
                foreach (Transform child in SettingArea.transform)
                {
                    Destroy(child.gameObject);
                }

                foreach (var setting in recive.settings)
                {
                    GameObject prefab;
                    switch (setting.Value.type)
                    {
                        case "checkbox":
                            Toggle toggle;

                            prefab = Instantiate(SettingPrefabCheckbox, SettingArea.transform) as GameObject;
                            toggle = prefab.GetComponentInChildren<Toggle>();
                            toggle.onValueChanged.AddListener(delegate { CheckboxOnChange(setting.Key, toggle); });
                        break;
                        case "slider":
                            Slider slider;
                            
                            prefab = Instantiate(SettingPrefabScroll, SettingArea.transform) as GameObject;
                            slider = prefab.GetComponentInChildren<Slider>();

                            Text text = prefab.transform.Find("Value").GetComponent<Text>();

                            if (setting.Value.min != null)
                                slider.minValue = (float)setting.Value.min;
                            if (setting.Value.max != null)
                                slider.maxValue = (float)setting.Value.max;

                            slider.onValueChanged.AddListener(delegate { ScrollOnChange(setting.Key, slider, text); });
                        break;
                        case "dropdown":
                            Dropdown dropdown;

                            prefab = Instantiate(SettingPrefabDropdown, SettingArea.transform) as GameObject;
                            dropdown = prefab.GetComponentInChildren<Dropdown>();

                            List<string> list = new List<string>(setting.Value.values.Values);
                            _lists[setting.Key] = new List<string>(setting.Value.values.Keys).ToArray();

                            dropdown.ClearOptions();
                            dropdown.AddOptions(list);
                            dropdown.onValueChanged.AddListener(delegate { DropdownOnChange(setting.Key, dropdown); });
                        break;
                        default:
                            Error("С сервера пришла настройка с остутвующим в клиенте типом " + setting.Value.type);
                        return;
                    }

                    prefab.name = setting.Key;
                    if (prefab.transform.Find("Title") != null && prefab.transform.Find("Title").GetComponent<Text>() != null)
                        prefab.transform.Find("Title").GetComponent<Text>().text = setting.Value.title;

                    _types[setting.Key] = setting.Value.type;
                }
            }

            base.HandleData(recive);
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            if (key == player_key && ((PlayerRecive)recive).components != null)
            {
                Dictionary<string, string> settings = ((PlayerRecive)recive).components.settings;
                if (settings != null)
                {
                    if (settings.ContainsKey("fps"))
                        Application.targetFrameRate = int.Parse(settings["fps"]);                      
                    
                    if(settings.ContainsKey("joystick"))
                        joystick.gameObject.SetActive(int.Parse(settings["joystick"])>0);

                    foreach (var setting in settings)
                    {
                        if (!_types.ContainsKey(setting.Key)) 
                        {
                            Error("С сервера пришла настройка с остутвующим в значением " + setting.Key);
                            return null;
                        }
                        switch (_types[setting.Key])
                        {
                            case "checkbox":
                                Toggle toggle = SettingArea.transform.Find(setting.Key).GetComponentInChildren<Toggle>();
                                toggle.isOn = (int.Parse(setting.Value) != 0 ? true : false);
                            break;                            
                            case "slider":
                                Slider slider = SettingArea.transform.Find(setting.Key).GetComponentInChildren<Slider>();
                                slider.value = float.Parse(setting.Value);
                            break;                           
                            case "dropdown":
                                Dropdown dropdown = SettingArea.transform.Find(setting.Key).GetComponentInChildren<Dropdown>();
                                dropdown.value = Array.IndexOf(_lists[setting.Key], setting.Value);
                            break;
                        }
                            
                        _settings[setting.Key] = setting.Value;
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
        
        private void DropdownOnChange(string key, Dropdown obj)
        {
            _settings[key] = _lists[key][obj.value];
        }

        public void Save()
        {
            SettingsResponse response = new SettingsResponse();
            response.settings = _settings;
            response.Send();
        }
    }
}