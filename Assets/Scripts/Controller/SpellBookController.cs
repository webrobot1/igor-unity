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
    abstract public class SpellBookController : UpdateController
    {
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

        public static Dictionary<string, Spell> spells;

        protected override void Awake()
        {
            spells = new Dictionary<string, Spell>();

            base.Awake();

            if (spellPrefab == null)
                Error("не указан префаб заклинания в книге");       
        }

        protected virtual void HandleData(NewRecive<PlayerRecive, EnemyRecive, ObjectRecive> recive)
        {
            if (recive.spellBook != null)
            {
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

                    spells.Add(spell.Key, prefab);
                }
            }

            base.HandleData(recive);
        }
    }
}