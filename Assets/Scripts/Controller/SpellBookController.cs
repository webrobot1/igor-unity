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
    abstract public class SpellBookController : UIController
    {
        /// <summary>Компонент префаба заклинания: группа команды, которой оно применяется.</summary>
        public const string COMPONENT_EVENT = "event";

        /// <summary>Компонент префаба заклинания: стоимость применения в мане.</summary>
        public const string COMPONENT_MP_COST = "mp_cost";

        /// <summary>
        /// Компонент существа: какие заклинания у него есть (ключ — префаб заклинания, значение — true).
        /// У своего игрока состав приходит пакетом, у чужой цели — нет: компонент приватный, и о ней
        /// известно лишь типовое для её вида из каталога префабов.
        /// </summary>
        public const string COMPONENT_SPELL_BOOK = "spell_book";

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

        /// <summary>
        /// Книга собирается из ДВУХ источников: состав — компонент spell_book своего игрока (какие заклинания
        /// выучены), а чем каждое является (название, описание, стоимость маны, группа команды) — каталог
        /// префабов, который клиент тянет до входа в мир. Отдельного справочника заклинаний сервер не шлёт:
        /// заклинание — обычный префаб, и его свойства лежат там же, где свойства прочего контента.
        ///
        /// Собирается в HandleData, до обхода сущностей пакета: панель быстрых действий ищет свои заклинания
        /// в готовой книге, а её контроллер стоит в цепочке ВЫШЕ этого — на обходе сущностей книга была бы
        /// ещё пуста, и панель ругалась бы на «не найдено заклинание».
        /// </summary>
        protected override void HandleData(NewRecive<PlayerRecive, CreatureRecive> recive)
        {
            PlayerRecive player = FindOwnPlayer(recive);

            if (player != null && player.components != null)
            {
                Dictionary<string, bool> book = player.components.spell_book;
                if (book != null)
                {
                    _spells = new Dictionary<string, Spell>();
                    foreach (Transform child in spellGroupArea)
                    {
                        Destroy(child.gameObject);
                    }

                    foreach (var spell in book)
                    {
                        if (!spell.Value)
                            continue;

                        JToken group = AnimationCacheService.GetComponentValue(spell.Key, COMPONENT_EVENT, null);
                        JToken cost  = AnimationCacheService.GetComponentValue(spell.Key, COMPONENT_MP_COST, null);

                        if (group == null)
                        {
                            Error("У заклинания " + spell.Key + " не задана группа команды (компонент " + COMPONENT_EVENT + ")");
                            return;
                        }

                        Spell prefab = Instantiate(spellPrefab, spellGroupArea) as Spell;

                        prefab.Magic = spell.Key;
                        prefab.@event = group.Value<string>();

                        prefab.title.text = AnimationCacheService.GetPrefabName(spell.Key) ?? spell.Key;
                        prefab.description.text = AnimationCacheService.GetPrefabDescription(spell.Key) ?? "";
                        prefab.ManaCost = cost != null ? cost.Value<int>() : 0;

                        _spells.Add(spell.Key, prefab);
                    }
                }
            }

            base.HandleData(recive);
        }

        /// <summary>
        /// Свой игрок в пакете мира: он приходит внутри своей карты, а карт в пакете несколько (соседние
        /// локации открытого мира). null — в этом пакете своего игрока нет (обычная дельта чужих сущностей).
        /// </summary>
        private PlayerRecive FindOwnPlayer(NewRecive<PlayerRecive, CreatureRecive> recive)
        {
            if (recive.world == null)
                return null;

            foreach (var map in recive.world)
                if (map.Value != null && map.Value.player != null && map.Value.player.TryGetValue(player_key, out PlayerRecive player))
                    return player;

            return null;
        }
    }
}