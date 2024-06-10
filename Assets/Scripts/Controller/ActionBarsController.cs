using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
    abstract public class ActionBarsController : SpellBookController
    {
        [Header("��� ������ � ���� �������� �������")]

        [SerializeField]
        private ActionBar[] _actionBars = new ActionBar[7];

        public ActionBar[] ActionBars
        {
            get { return _actionBars; }
            set { }
        }

        /// <summary>
        /// �������������� ������� �������� ������� (���������)
        /// </summary>
        [SerializeField]
        protected GameObject onlyMobileActions;

        protected override void Awake()
        {
            base.Awake();

            if (onlyMobileActions == null)
            {
                Error("�� ���� ���������� ������� ������� ������� ������������ ������ ��� ��������� ������");
                return;
            }
              
            
            if (_actionBars.Length != 7)
            {
                Error("�� ���� ���������� ������� ������� �������  ������ ��������� 7 ���������");
                return;
            }

            for (int i = 0; i < 7; i++)
            {
                if (_actionBars[i] == null)
                {
                    Error("�� ������ GameObject ������ �������� ������� ��� ������� "+ i);
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
                            Error("������ ����� ������� ������� " + action.Key + " ������ ��������� � ���������� ����� ���� " + _actionBars.Length);
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
                                    Error("�� ������� ���������� " + action.Value.id + " ������������� �� ������� ������� " + action.Key);
                                    return null;
                                }
                                    
                                _actionBars[action.Key - 1].Item = Spells[action.Value.id];

                                player.Log("������� ������� "+ action.Key + ": �������� ������ ����������� � ������� " + action.Value.id);
                            break;
                            default:
                                Error("����������� ��� ������� ������� '" + action.Value.type + "' ��� ������� " + action.Key);
                            return null;
                        }
                    }
                }
            }
            return base.UpdateObject(map_id, key, recive, type);
        }
    }
}