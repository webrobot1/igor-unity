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

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
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

            GameObject result = base.UpdateObject(map_id, key, recive, type);

            if (enemy != null)
            {
                if (hpBefore != null && enemy.hp != null && enemy.hp != hpBefore)
                {
                    int delta = (int)enemy.hp - (int)hpBefore;
                    if (delta < 0)
                        CreateCombatText(enemy.transform.position, -delta, CombatTextType.DAMAGE);
                    else if (delta > 0)
                        CreateCombatText(enemy.transform.position, delta, CombatTextType.HEAL);
                }

                if (mpBefore != null && enemy.mp != null && enemy.mp != mpBefore)
                {
                    int delta = (int)enemy.mp - (int)mpBefore;
                    if (delta < 0)
                        CreateCombatText(enemy.transform.position, -delta, CombatTextType.MANA);
                }
            }

            return result;
        }

        private void CreateCombatText(Vector3 worldPosition, int value, CombatTextType type)
        {
            worldPosition.y += 0.8f;

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
