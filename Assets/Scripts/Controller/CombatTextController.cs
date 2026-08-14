using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    abstract public class CombatTextController : SettingsController
    {
        [Header("Боевой текст")]
        [SerializeField]
        private Transform combatTextParent;

        [SerializeField]
        private GameObject combatTextPrefab;

        protected override void Awake()
        {
            base.Awake();

            if (combatTextParent == null)
            {
                Error("не присвоен Transform контейнер для боевого текста");
                return;
            }

            if (combatTextPrefab == null)
            {
                Error("не присвоен префаб боевого текста");
                return;
            }
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive)
        {
            EnemyModel enemy = null;
            int? hpBefore = null;
            int? mpBefore = null;
            GameObject existing = GameObject.Find(key);
            if (existing != null)
            {
                enemy = existing.GetComponent<EnemyModel>();
                if (enemy != null)
                {
                    hpBefore = enemy.hp;
                    mpBefore = enemy.mp;
                }
            }

            GameObject result = base.UpdateObject(map_id, key, recive);

            if (enemy != null)
            {
                int hpDelta = hpBefore != null && enemy.hp != null ? (int)enemy.hp - (int)hpBefore : 0;
                int mpDelta = mpBefore != null && enemy.mp != null ? (int)enemy.mp - (int)mpBefore : 0;

                if (hpDelta != 0)
                    CreateCombatText(enemy.transform, Mathf.Abs(hpDelta),
                        hpDelta < 0 ? CombatTextType.DAMAGE : CombatTextType.HEAL);

                if (mpDelta < 0)
                    CreateCombatText(enemy.transform, -mpDelta, CombatTextType.MANA);
            }

            return result;
        }

        // Шаг разноса соседних по времени надписей по X (в клетках карты) и окно, внутри которого надписи
        // считаются соседними.
        private const float NEIGHBOUR_SHIFT = 0.4f;
        private const float NEIGHBOUR_WINDOW = 0.3f;

        /// <summary>
        /// Надписи одной сущности, появившиеся почти одновременно (лечение за ману: сервер везёт прибавку
        /// здоровья и списание маны РАЗНЫМИ обновлениями сущности, приходящими в одном кадре), стартуют из
        /// одной точки и летят с одной скоростью — то есть полностью перекрывают друг друга. Потому разносим
        /// по горизонтали не по дельтам одного пакета, а по соседству ЖИВЫХ надписей этой сущности во времени.
        /// </summary>
        private void CreateCombatText(Transform owner, int value, CombatTextType type)
        {
            int neighbours = 0;
            foreach (Transform child in combatTextParent)
            {
                CombatText ct = child.GetComponent<CombatText>();
                if (ct != null && ct.Owner == owner && Time.time - ct.Born < NEIGHBOUR_WINDOW)
                    neighbours++;
            }

            // 0 → по центру, дальше попеременно вправо-влево с нарастающим шагом.
            float shiftX = neighbours == 0
                ? 0f
                : (neighbours % 2 == 1 ? 1f : -1f) * NEIGHBOUR_SHIFT * ((neighbours + 1) / 2);

            Vector3 worldPosition = owner.position;
            worldPosition.y += 0.8f;
            worldPosition.x += shiftX;

            GameObject go = Instantiate(combatTextPrefab, worldPosition, Quaternion.identity, combatTextParent);
            go.GetComponent<CombatText>().Init(owner);
            Text text = go.GetComponentInChildren<Text>();

            string prefix;
            switch (type)
            {
                case CombatTextType.HEAL:
                    prefix = "+";
                    text.color = Color.green;
                    break;
                case CombatTextType.MANA:
                    prefix = "-";
                    text.color = Color.blue;
                    break;
                default:
                    prefix = "-";
                    text.color = Color.red;
                    break;
            }

            text.text = prefix + value;
        }
    }
}
