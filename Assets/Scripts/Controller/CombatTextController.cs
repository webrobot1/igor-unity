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

                // Один пакет меняет и жизнь, и ману (лечение за ману) — надписи стартуют из одной точки и летят
                // с одной скоростью, то есть полностью перекрывают друг друга. Разводим по горизонтали.
                float shift = hpDelta != 0 && mpDelta < 0 ? SIMULTANEOUS_SHIFT : 0f;

                if (hpDelta != 0)
                    CreateCombatText(enemy.transform.position, Mathf.Abs(hpDelta),
                        hpDelta < 0 ? CombatTextType.DAMAGE : CombatTextType.HEAL, -shift);

                if (mpDelta < 0)
                    CreateCombatText(enemy.transform.position, -mpDelta, CombatTextType.MANA, shift);
            }

            return result;
        }

        // Полуразнос двух одновременных надписей по X (в клетках карты)
        private const float SIMULTANEOUS_SHIFT = 0.4f;

        private void CreateCombatText(Vector3 worldPosition, int value, CombatTextType type, float shiftX = 0f)
        {
            worldPosition.y += 0.8f;
            worldPosition.x += shiftX;

            GameObject go = Instantiate(combatTextPrefab, worldPosition, Quaternion.identity, combatTextParent);
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
