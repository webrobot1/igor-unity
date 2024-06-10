using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WebGLSupport;

namespace Mmogick
{
    /// <summary>
	/// Класс для обновления Меню настрое игрока
	/// </summary>
    abstract public class SettingsController : CursorController
    {
        [Header("Для работы с меню настроек")]

        /// <summary>
        /// поле для генерации объектов настроек
        /// </summary>
        [SerializeField]
        private Button saveSettingsButton;

        /// <summary>
        /// поле для генерации объектов настроек
        /// </summary>
        [SerializeField]
        private Transform settingArea;

        /// <summary>
        /// префабы блоков настроек - чекбокс
        /// </summary>
        [SerializeField]
        private GameObject settingPrefabCheckbox;

        /// <summary>
        /// префабы блоков настроек - скроллинг
        /// </summary>
        [SerializeField]
        private GameObject settingPrefabScroll;        
        
        /// <summary>
        /// префабы блоков настроек - выпадающий список
        /// </summary>
        [SerializeField]
        private GameObject settingPrefabDropdown;

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

            if (saveSettingsButton == null)
            {
                Error("не указана кнопка сохранения настроек");
                return;
            }
                
            saveSettingsButton.onClick.AddListener(delegate { SaveSettings(); });

            if (settingArea == null)
            {
                Error("не указан transform области где будут выводится настройки с сервера");
                return;
            }
                          
            if (!settingArea.IsChildOf(settingGroup.transform))
            {
                Error("указанный объект Transform книги заклинаний книги на которую буду загружаться с сервера заклинаний не является часть CanvasGroup указанной как книга заклинаний");
                return;
            }
                
            if (settingPrefabCheckbox == null)
            {
                Error("не указан prefab для настройки типа Checkbox");
                return;
            }
                                
            if (settingPrefabScroll == null)
            {
                Error("не указан prefab для настройки типа Scroll");
                return;
            }
                              
            if (settingPrefabDropdown == null)
            {
                Error("не указан prefab для настройки типа DropDown меню");
                return;
            }
                
          
#if UNITY_WEBGL && !UNITY_EDITOR
                 WebGLRotation.Rotation(1);
#else
            Screen.orientation = ScreenOrientation.LandscapeRight;
            Screen.autorotateToPortrait = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
#endif           
        }

        protected override void HandleData(NewRecive<PlayerRecive, EnemyRecive, ObjectRecive> recive)
        {
            if (recive.settings != null)
            {
                foreach (Transform child in settingArea)
                {
                    Destroy(child.gameObject);
                }

                _types = new Dictionary<string, string>();
                foreach (var setting in recive.settings)
                {
                    GameObject prefab;
                    switch (setting.Value.type)
                    {
                        case "checkbox":
                            Toggle toggle;

                            prefab = Instantiate(settingPrefabCheckbox, settingArea) as GameObject;
                            toggle = prefab.GetComponentInChildren<Toggle>();
                            toggle.onValueChanged.AddListener(delegate { CheckboxOnChange(setting.Key, toggle); });
                        break;
                        case "slider":
                            Slider slider;
                            
                            prefab = Instantiate(settingPrefabScroll, settingArea) as GameObject;
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

                            prefab = Instantiate(settingPrefabDropdown, settingArea) as GameObject;
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

                    if (settings.ContainsKey("joystick"))
                        joystick.gameObject.SetActive(int.Parse(settings["joystick"]) > 0);

                    if (settings.ContainsKey("actions"))
                        onlyMobileActions.gameObject.SetActive(settings["actions"] == "mobile");

                    foreach (var setting in settings)
                    {
                        if (!_types.ContainsKey(setting.Key))
                        {
                            Error("С сервера пришла настройка " + setting.Key + " со значением "+ setting.Value + ", но отсутвует ее параметры ");
                            return null;
                        }
                        switch (_types[setting.Key])
                        {
                            case "checkbox":
                                Toggle toggle = settingArea.Find(setting.Key).GetComponentInChildren<Toggle>();
                                toggle.isOn = (int.Parse(setting.Value) != 0 ? true : false);
                                break;
                            case "slider":
                                Slider slider = settingArea.Find(setting.Key).GetComponentInChildren<Slider>();
                                slider.value = float.Parse(setting.Value);
                                slider.onValueChanged.Invoke(slider.value);
                                break;
                            case "dropdown":
                                Dropdown dropdown = settingArea.Find(setting.Key).GetComponentInChildren<Dropdown>();
                                dropdown.value = Array.IndexOf(_lists[setting.Key], setting.Value);
                                dropdown.onValueChanged.Invoke(dropdown.value);
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
            if (text!=null)
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

        private void SaveSettings()
        {
            SettingsResponse response = new SettingsResponse();
            response.settings = _settings;
            response.Send();
        }
    }
}