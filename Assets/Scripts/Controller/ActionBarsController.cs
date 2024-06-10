using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
    abstract public class ActionBarsController : SpellBookController
    {
        [Header("Для работы с меню быстрого доступа")]

        [SerializeField]
        private ActionBar[] _actionBars = new ActionBar[7];

        public ActionBar[] ActionBars
        {
            get { return _actionBars; }
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
              
            
            if (_actionBars.Length != 7)
            {
                Error("не блок содержащий кнокпки бстрого доступа  должен содержать 7 элементов");
                return;
            }

            for (int i = 0; i < 7; i++)
            {
                if (_actionBars[i] == null)
                {
                    Error("не указан GameObject кнопки быстрого доступа под нмоером "+ i);
                    return;
                }
                    
                _actionBars[i].num = i+1;
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            if (key == player_key && ((PlayerRecive)recive).components != null)
            {
                Dictionary<int, ActionBarsRecive> actionbars = ((PlayerRecive)recive).components.actionbars;
                if (actionbars != null)
                {
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