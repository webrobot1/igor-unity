using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
    abstract public class ActionBarsController : SpellBookController
    {
        [Header("Для работы с меню быстрого доступа")]

        [SerializeField]
        private ActionBar[] _mobileActionBars = new ActionBar[3];

        [SerializeField]
        private Transform mainActionBarsContainer;

        [SerializeField]
        private GameObject actionButtonPrefab;

        private ActionBar[] _actionBars;

        private const int MOBILE_ACTION_COUNT = 3;

        public ActionBar[] ActionBars
        {
            get { return _actionBars ?? Array.Empty<ActionBar>(); }
            set { }
        }

        /// <summary>
        /// дополнительные кнокпки быстрого доступа (скрваемые)
        /// </summary>
        [SerializeField]
        protected GameObject onlyMobileActions;

        protected override void Awake()
        {
            base.Awake();

            if (onlyMobileActions == null)
            {
                Error("не блок содержащий кнокпки бстрого доступа отображаемый только для мобильной версии");
                return;
            }

            if (mainActionBarsContainer == null)
            {
                Error("не указан контейнер для основных кнопок быстрого доступа (MainActionBars)");
                return;
            }

            if (actionButtonPrefab == null)
            {
                Error("не указан префаб кнопки быстрого доступа (ActionButton)");
                return;
            }

            for (int i = 0; i < MOBILE_ACTION_COUNT; i++)
            {
                if (_mobileActionBars[i] == null)
                {
                    Error("не указана мобильная кнопка быстрого доступа под номером " + i);
                    return;
                }
            }
        }

        private void InitializeActionBars(int totalSlots)
        {
            if (_actionBars != null) return;

            int mainSlots = totalSlots - MOBILE_ACTION_COUNT;

            foreach (Transform child in mainActionBarsContainer)
                Destroy(child.gameObject);

            _actionBars = new ActionBar[totalSlots];

            for (int i = 0; i < mainSlots; i++)
            {
                GameObject obj = Instantiate(actionButtonPrefab, mainActionBarsContainer);
                obj.name = "ActionButton" + i;
                ActionBar bar = obj.GetComponentInChildren<ActionBar>();
                bar.num = i + 1;
                bar.SetTooltip(tooltip);
                _actionBars[i] = bar;
            }

            for (int i = 0; i < MOBILE_ACTION_COUNT; i++)
            {
                _mobileActionBars[i].num = mainSlots + i + 1;
                _mobileActionBars[i].SetTooltip(tooltip);
                _actionBars[mainSlots + i] = _mobileActionBars[i];
            }

        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            if (key == player_key && ((PlayerRecive)recive).components != null)
            {
                Dictionary<int, ActionBarsRecive> actionbars = ((PlayerRecive)recive).components.actionbars;
                if (actionbars != null)
                {
                    if (_actionBars == null)
                        InitializeActionBars(actionbars.Count);

                    foreach (var action in actionbars)
                    {
                        if (action.Key == 0 || action.Key > _actionBars.Length)
                        {
                            Error("Пришел номер быстрый клавиши " + action.Key + " однако настроено в клиентской части лишь " + _actionBars.Length);
                            return null;
                        }

                        switch (action.Value.type)
                        {
                            case "":
                                _actionBars[action.Key - 1].Item = null;
                            break;
                            case "spell":
                                if (!Spells.ContainsKey(action.Value.id))
                                {
                                    Error("не найдено заклинание " + action.Value.id + " установленное на быструю клавишу " + action.Key);
                                    return null;
                                }

                                _actionBars[action.Key - 1].Item = Spells[action.Value.id];

                                player.Log("Быстрая клавиша "+ action.Key + ": обновили данные заклинанием с сервера " + action.Value.id);
                            break;
                            default:
                                Error("Неизвестный тип быстрой клавиши '" + action.Value.type + "' под номером " + action.Key);
                            return null;
                        }
                    }
                }
            }
            return base.UpdateObject(map_id, key, recive, type);
        }
    }
}