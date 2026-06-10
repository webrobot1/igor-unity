using System.Collections.Generic;
using UnityEngine;
using SpriterDotNetUnity;

namespace Mmogick
{
    // Вешает внешние спрайты-предметы (оружие/щит) на Spriter-точки скелета сущности.
    // Точка (PointTransform) двигается каждый кадр вместе с костью, к которой привязана в SCML
    // (UnityAnimator.ApplyPointTransform ставит ей разрешённые позицию+угол), поэтому предмет —
    // ребёнок точки — автоматически следует за анимацией без покадровой логики позиционирования.
    //
    // Re-attach в LateUpdate, потому что: (1) SpriterDotNetBehaviour добавляется на GO асинхронно
    // (после загрузки скелета), (2) точка именуется/активируется только в кадрах, где присутствует
    // в FrameData. Держим предмет приклеенным, пока точка активна; иначе прячем.
    public class WeaponMount : MonoBehaviour
    {
        private const float Ppu = 100f;          // как в AnimationCacheService.GetSprite / SpriterDotNetBehaviour
        private const int SortingOrder = 1000;   // MVP: поверх тела; точный z по направлениям — Этап 2

        private class Mounted
        {
            public string pointName;
            public GameObject go;
            public Sprite sprite;   // созданный grip-pivot спрайт (отдельный от иконки) — освобождаем при замене/снятии
            public float ox, oy, angle, scale;
        }

        private readonly Dictionary<string, Mounted> _slots = new Dictionary<string, Mounted>();
        private SpriterDotNetBehaviour _beh;

        // Надеть/обновить предмет в слоте: pointName — имя Spriter-точки (object.name из object_slot),
        // pivotX/pivotY — точка хвата на картинке (0..1, центр вращения), к ней крепится якорь.
        // scale — ЦЕЛЕВОЙ МИРОВОЙ масштаб предмета (слот.scale × ground-нормализация): тот же размер,
        // что у prefab'а на земле. Композицию делает EquipmentController.SyncWeapon; bodyScale носителя
        // компенсируется в LateUpdate (предмет — внук нормализованной Metadata-ветки скелета).
        public void Apply(string slot, string pointName, Sprite sprite, float pivotX, float pivotY, float ox, float oy, float angle, float scale)
        {
            if (sprite == null || string.IsNullOrEmpty(pointName)) { Detach(slot); return; }

            if (!_slots.TryGetValue(slot, out Mounted m))
            {
                m = new Mounted { go = new GameObject("Weapon_" + slot) };
                m.go.AddComponent<SpriteRenderer>();
                _slots[slot] = m;
            }
            m.pointName = pointName; m.ox = ox; m.oy = oy; m.angle = angle; m.scale = scale;

            // Спрайт оружия с pivot ХВАТА (а не центр-pivot иконки): пересоздаём из текстуры исходника.
            // Тогда к точке крепится рукоять и оружие вращается вокруг неё.
            Texture2D tex = sprite.texture;
            Sprite grip = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(pivotX, pivotY), Ppu, 0, SpriteMeshType.FullRect);

            SpriteRenderer sr = m.go.GetComponent<SpriteRenderer>();
            sr.sprite = grip;
            sr.sortingOrder = SortingOrder;
            if (m.sprite != null) Destroy(m.sprite);   // освободить ранее созданный grip-спрайт
            m.sprite = grip;
        }

        public void Detach(string slot)
        {
            if (_slots.TryGetValue(slot, out Mounted m))
            {
                if (m.go != null) Destroy(m.go);
                if (m.sprite != null) Destroy(m.sprite);
                _slots.Remove(slot);
            }
        }

        private void LateUpdate()
        {
            if (_slots.Count == 0) return;
            if (_beh == null) _beh = GetComponent<SpriterDotNetBehaviour>();

            foreach (Mounted m in _slots.Values)
            {
                if (m.go == null) continue;
                Transform pt = FindPoint(_beh, m.pointName);
                if (pt == null) { m.go.SetActive(false); continue; }   // точка не активна в этом кадре

                m.go.SetActive(true);
                if (m.go.transform.parent != pt) m.go.transform.SetParent(pt, false);
                m.go.transform.localPosition = new Vector3(m.ox / Ppu, m.oy / Ppu, 0f);
                m.go.transform.localEulerAngles = new Vector3(0f, 0f, m.angle);
                // m.scale — целевой МИРОВОЙ масштаб (как на земле). Точка (pt) живёт под нормализованной
                // Metadata-веткой скелета, её lossyScale.y = bodyScale носителя. Делим на него, иначе предмет
                // унаследовал бы это сжатие и в руке был бы в bodyScale раз мельче того же prefab'а на земле
                // (на giant — ещё мельче). Якорь не масштабируется (Point.localScale=1), root.y=1 → lossyScale.y чист.
                float body = Mathf.Abs(pt.lossyScale.y);
                if (body < 0.0001f) body = 1f;
                float s = m.scale / body;
                m.go.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        // Точка переименовывается в SCML-имя (point.name = name) и активируется только в кадрах,
        // где присутствует в FrameData. Признак валидности = activeSelf && name совпадает.
        private static Transform FindPoint(SpriterDotNetBehaviour beh, string pointName)
        {
            ChildData cd = beh != null ? beh.ChildData : null;
            if (cd == null || cd.Points == null) return null;
            for (int i = 0; i < cd.Points.Length; i++)
                if (cd.Points[i] != null && cd.Points[i].activeSelf && cd.Points[i].name == pointName)
                    return cd.PointTransforms[i];
            return null;
        }
    }
}
