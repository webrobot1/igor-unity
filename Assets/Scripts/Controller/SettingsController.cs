using Newtonsoft.Json.Linq;
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
    abstract public class SettingsController : QuantityPromptController
    {
        /// <summary>Компонент игрока, чьё умолчание задаёт схему настроек (типы, заголовки, границы).</summary>
        public const string COMPONENT_SETTINGS = "settings";

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

        /// <summary>
        /// Схема настроек (какие они бывают, как называются и в каких границах) — умолчание компонента settings
        /// у префаба игрока: клиент берёт его из каталога префабов, который тянет до входа в мир. Значения же
        /// приходят своим каналом, компонентами игрока (UpdateObject ниже), и схема обязана быть построена
        /// раньше них — иначе применять значения не к чему.
        /// Пересборка идёт при каждом появлении своего игрока: за сессию схема не меняется, а вот окно после
        /// пере-входа собирается заново.
        /// </summary>
        private void BuildSettingsSchema(string prefabSlug)
        {
            JToken schema = AnimationCacheService.GetComponentValue(prefabSlug, COMPONENT_SETTINGS, null);
            if (schema == null)
                return;

            Dictionary<string, SettingRecive> settings =
                schema.ToObject<Dictionary<string, SettingRecive>>();

            if (settings == null)
                return;

            foreach (Transform child in settingArea)
            {
                Destroy(child.gameObject);
            }

            _types = new Dictionary<string, string>();
            foreach (var setting in settings)
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

        /// <summary>
        /// Тестовый режим игрока (настройка «Тестовый режим»): открывает служебные блоки интерфейса —
        /// отладочные слои карты и счётчики частоты, задержки и номера карты — и клиентские логи. Сама
        /// настройка приходит с сервера как обычная галочка; что именно показывать, решают наследники,
        /// у которых эти блоки есть.
        /// </summary>
        protected virtual void SetTestMode(bool enabled)
        {
            TestMode = enabled;

            // Логи собранной игры: обычному игроку консоль не нужна, отлаживающему нужна — решает его же
            // настройка. В редакторе и dev-билде логи не гасим: там их читает разработчик, а не игрок,
            // и клиент логирует всю сессию (BaseController.Awake).
            #if !UNITY_EDITOR && !DEVELOPMENT_BUILD
                Debug.unityLogger.logEnabled = enabled;
            #endif
        }

        /// <summary>
        /// Включён ли тестовый режим игрока. Настройка приходит с сервера и применяется наследниками к их
        /// блокам; здесь она же лежит одним читаемым значением — для служебных показов вне UI-контроллеров
        /// (отсчёты над телами и подобное), которым не к чему привязаться активностью панели.
        /// </summary>
        public static bool TestMode { get; private set; }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
            if (key == player_key && ((PlayerRecive)recive).components != null)
            {
                Dictionary<string, string> settings = ((PlayerRecive)recive).components.settings;
                if (settings != null)
                {
                    // Схема — из каталога префабов, и строится ровно перед применением значений: значения
                    // раскладываются по её типам, без неё раскладывать не по чему. Пере-собирается только
                    // при появлении своего игрока (там приходит prefab); на дельте значений её не трогаем —
                    // иначе окно настроек пересобиралось бы на каждое переключение галочки.
                    if (_types == null || _types.Count == 0)
                        BuildSettingsSchema(recive.prefab);

                    if (settings.ContainsKey("fps"))
                        Application.targetFrameRate = int.Parse(settings["fps"]);

                    // Джойстик скрыт с запуска (CursorController.Awake) — тот же случай, что у галочки
                    // «Тестовый режим» ниже: пришедшее значение решает, а не пришедшее у НЕ объявленной
                    // игрой настройки оставляет блок скрытым, тогда как просто не менявшееся — как было.
                    if (settings.ContainsKey("joystick"))
                        joystick.gameObject.SetActive(int.Parse(settings["joystick"]) > 0);

                    if (settings.ContainsKey("actions"))
                        onlyMobileActions.gameObject.SetActive(settings["actions"] == "mobile");

                    if (settings.ContainsKey("minimap"))
                        SetMinimapEnabled(int.Parse(settings["minimap"]) > 0);

                    // Пакет несёт РАЗНИЦУ значений, поэтому отсутствие ключа тут значит «не менялось» —
                    // объявлена настройка игрой или нет, говорит схема. Для галочки «Тестовый режим» это
                    // важно: вход в игру включает логи собранного билда (BaseController), и не объявленная
                    // игрой галочка обязана их погасить, а просто не пришедшая — оставить как было.
                    if (settings.ContainsKey("debug"))
                        SetTestMode(int.Parse(settings["debug"]) > 0);
                    else if (!_types.ContainsKey("debug"))
                        SetTestMode(false);

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

            return base.UpdateObject(map_id, key, recive);
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