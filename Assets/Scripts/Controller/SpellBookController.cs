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
    abstract public class SpellBookController : UIController
    {
        [Header("Для работы с книгой заклинаний")]

        /// <summary>
        /// префаб заклинания в книге
        /// </summary>
        [SerializeField]
        private Spell spellPrefab;       
        
        /// <summary>
        /// префаб заклинания в книге
        /// </summary>
        [SerializeField]
        private Transform spellGroupArea;

        /// <summary>
        /// список доступных заклинаний с их характеристиками 
        /// </summary>
        private static Dictionary<string, Spell> _spells;

        public Dictionary<string, Spell> Spells
        {
            get { return _spells; }
            set { }
        }

        protected override void Awake()
        {
            base.Awake();
           
            // объявлять тут тк мы используем в unity Editor опцию при который вызод из play моде НЕ очищает статику (зато быстро выходит и заходит, но надо очищать вручную везде в Awake)
            _spells = new Dictionary<string, Spell>();
            if (spellPrefab == null) 
            { 
                Error("не указан префаб заклинания в книге");
                return;
            }
                              
            if (spellGroupArea == null) 
            { 
                Error("не указан Transform книги на которую буду загружаться с сервера заклинаний");
                return;
            }
                
            if (!spellGroupArea.IsChildOf(spellGroup.transform)) 
            {  
                Error("указанный объект Transform книги заклинаний книги на которую буду загружаться с сервера заклинаний не является часть CanvasGroup указанной как книга заклинаний");
                return;
            }
               
        }

        protected override void HandleData(NewRecive<PlayerRecive, EnemyRecive, ObjectRecive> recive)
        {
            if (recive.spellBook != null)
            {
                // если книга заклинаний пришла по новой с сервера
                _spells = new Dictionary<string, Spell>();
                foreach (Transform child in spellGroupArea)
                {
                    Destroy(child.gameObject);
                }

                foreach (var spell in recive.spellBook)
                {
                    Spell prefab = Instantiate(spellPrefab, spellGroupArea) as Spell;

                    prefab.Magic = spell.Key;
                    prefab.group = spell.Value.group;

                    prefab.title.text = spell.Value.name;
                    prefab.description.text = spell.Value.description;
                    prefab.mp.text = spell.Value.mp.ToString();

                    _spells.Add(spell.Key, prefab);
                }
            }

            base.HandleData(recive);
        }
    }
}