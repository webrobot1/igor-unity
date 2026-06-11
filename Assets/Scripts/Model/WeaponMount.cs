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
    // У слота НЕСКОЛЬКО якорей (per-direction: своя кость на ракурс) — направление задаёт сам клип,
    // активен тот якорь, чья точка присутствует в текущем кадре (FindPoint). Перебор в LateUpdate,
    // потому что: (1) SpriterDotNetBehaviour добавляется на GO асинхронно (после загрузки скелета),
    // (2) точка именуется/активируется только в кадрах, где присутствует в FrameData.
    //
    // Глубина: предмет кладётся на sortingOrder = base скелета + z якоря (z — draw-rank кожи его
    // кости из object_slot, тот же индекс, что UnityAnimator раздаёт частям тела) и приподнимается
    // над кожей подъёмом по Z (ZLift) — части с бо́льшим рангом (голова над торсом) остаются поверх.
    public class WeaponMount : MonoBehaviour
    {
        private const float Ppu = 100f;          // как в AnimationCacheService.GetSprite / SpriterDotNetBehaviour
        private const int FallbackOrder = 1000;  // якорь без z (у кости нет кожи): поверх всего внутри SortingGroup

        // Тай-брейк при РАВНОМ sortingOrder с кожей якоря — CustomAxis (0,1,-1) (Startup.cs):
        // дальше тот, у кого больше dot = y − z, поэтому предмет ПОДНИМАЕМ по z. Запас в 1 юнит
        // покрывает разницу y между позицией предмета и pivot'ом спрайта кожи (доли юнита);
        // за пределы SortingGroup скелета z не влияет — группа сортируется с миром как единое целое.
        private const float ZLift = 1f;

        // Один якорь слота: Spriter-точка + позиционирование предмета относительно неё.
        public class Anchor
        {
            public string pointName; // имя точки (object.name из object_slot)
            public float ox, oy;     // сдвиг от точки, px
            public float scale;      // ЦЕЛЕВОЙ МИРОВОЙ масштаб (слот.scale × ground-нормализация, см. SyncWeapon)
            public float? angle;     // null = «как загружено»: предмет не доворачивается к кости (мировой upright)
            public int? z;           // draw-rank кожи кости-якоря (object_slot.z); null → FallbackOrder
        }

        // Источник одного варианта картинки для Apply: исходный спрайт из кеша (центр-pivot) + хват + направление.
        public struct VariantSource
        {
            public int angle;        // направление, под которое нарисован вариант (0=вправо, 90=вверх, …)
            public Sprite sprite;
            public float pivotX, pivotY;
        }

        // Вариант после Apply: направление → готовый grip-спрайт (pivot хвата).
        private class Variant
        {
            public int angle;
            public Sprite grip;      // создаётся в Apply, освобождается в Detach
        }

        private class Mounted
        {
            public Anchor[] anchors;
            public Variant[] variants;   // ≥1, в серверном порядке (angle ASC); активный выбирается по forward
            public string rotationMode;  // AnimationCacheService.RotationMode.* (mirror_x даёт зеркальных кандидатов)
            public GameObject go;
            public SpriteRenderer sr;
            public int curVariant = -1;  // кэш выбора: спрайт подменяется только при смене варианта/флипа
            public bool curFlip;
        }

        private readonly Dictionary<string, Mounted> _slots = new Dictionary<string, Mounted>();
        private SpriterDotNetBehaviour _beh;
        private EntityModel _em;   // forward носителя — для выбора варианта картинки по направлению

        // Надеть/обновить предмет в слоте: anchors — все якоря слота (активный выбирается по кадру),
        // variants — все варианты картинки по направлениям (активный выбирается по forward в LateUpdate),
        // rotationMode — AnimationCacheService.RotationMode.* (mirror_x добавляет зеркальных кандидатов).
        // Grip-спрайты (pivot = хват, 0..1, центр вращения) пересоздаются из текстур исходников один раз
        // здесь — не в LateUpdate (Sprite.Create аллоцирует). bodyScale носителя компенсируется в LateUpdate.
        public void Apply(string slot, Anchor[] anchors, VariantSource[] variants, string rotationMode)
        {
            Detach(slot);   // пересоздаём с нуля: освобождает прежние grip-спрайты (набор мог измениться)
            if (anchors == null || anchors.Length == 0 || variants == null || variants.Length == 0)
                return;

            Mounted m = new Mounted { go = new GameObject("Weapon_" + slot) };
            m.sr = m.go.AddComponent<SpriteRenderer>();
            m.anchors = anchors;
            m.rotationMode = rotationMode;
            m.variants = new Variant[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                Texture2D tex = variants[i].sprite.texture;
                m.variants[i] = new Variant
                {
                    angle = variants[i].angle,
                    grip = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(variants[i].pivotX, variants[i].pivotY), Ppu, 0, SpriteMeshType.FullRect),
                };
            }
            // sr.sprite/sortingOrder — в LateUpdate (зависят от активного якоря и forward); LateUpdate
            // выполняется до рендера кадра, пустого первого кадра не будет.
            _slots[slot] = m;
        }

        public void Detach(string slot)
        {
            if (_slots.TryGetValue(slot, out Mounted m))
            {
                if (m.go != null) Destroy(m.go);
                if (m.variants != null)
                    foreach (Variant v in m.variants)
                        if (v != null && v.grip != null) Destroy(v.grip);
                _slots.Remove(slot);
            }
        }

        // Выбор варианта картинки под forward. Экранный угол кандидата учитывает зеркало ТЕЛА (флип корня
        // h_mirror — предмет-потомок зеркалится вместе с телом, второй раз зеркалить нельзя) и собственный
        // flipX предмета (только mirror_x: лево из права): screen = (mirrored XOR flip) ? 180−angle : angle.
        // Побеждает кандидат с минимальной |DeltaAngle(forward, screen)|; для none/free flip-кандидатов нет.
        private static void PickVariant(Mounted m, float fwdDeg, bool mirrored, out int best, out bool flip)
        {
            best = 0; flip = false;
            float bestDist = float.MaxValue;
            int fMax = m.rotationMode == AnimationCacheService.RotationMode.MirrorX ? 1 : 0;
            for (int i = 0; i < m.variants.Length; i++)
            {
                for (int f = 0; f <= fMax; f++)
                {
                    float screen = (mirrored ^ (f == 1)) ? Mathf.Repeat(180f - m.variants[i].angle, 360f) : m.variants[i].angle;
                    float d = Mathf.Abs(Mathf.DeltaAngle(fwdDeg, screen));
                    if (d < bestDist) { bestDist = d; best = i; flip = f == 1; }
                }
            }
        }

        private void LateUpdate()
        {
            if (_slots.Count == 0) return;
            if (_beh == null) _beh = GetComponent<SpriterDotNetBehaviour>();
            if (_em == null) _em = GetComponent<EntityModel>();

            foreach (Mounted m in _slots.Values)
            {
                if (m.go == null) continue;

                // Активный якорь = первый, чья точка есть в кадре (клип направления содержит только свои кости).
                Anchor a = null;
                Transform pt = null;
                for (int i = 0; i < m.anchors.Length && pt == null; i++)
                {
                    pt = FindPoint(_beh, m.anchors[i].pointName);
                    a = m.anchors[i];
                }
                if (pt == null) { m.go.SetActive(false); continue; }   // ни одна точка не активна в этом кадре

                m.go.SetActive(true);

                // Вариант картинки под forward носителя. mirrored — тело отзеркалено флипом корня
                // (h_mirror): знак мирового X-масштаба точки включает все родительские флипы.
                bool mirrored = pt.lossyScale.x < 0f;
                Vector3 fwd = _em != null ? _em.Forward : Vector3.right;
                if (fwd.x == 0f && fwd.y == 0f) fwd = Vector3.right;
                PickVariant(m, Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg, mirrored, out int vi, out bool flip);
                if (vi != m.curVariant || flip != m.curFlip)
                {
                    m.curVariant = vi;
                    m.curFlip = flip;
                    m.sr.sprite = m.variants[vi].grip;
                    m.sr.flipX = flip;   // флип вокруг pivot'а (хвата) — рукоять остаётся на якоре
                }
                if (m.go.transform.parent != pt) m.go.transform.SetParent(pt, false);
                m.go.transform.localPosition = new Vector3(a.ox / Ppu, a.oy / Ppu, ZLift);
                // angle задан — предмет следует за костью: нарисованное направление варианта сначала
                // нормализуется к канону (вправо), затем доворачивается slot.angle относительно точки —
                // предметы, нарисованные в любую сторону (но с честным image.angle), ведут себя одинаково.
                // Экранное направление флипнутого кандидата — 180−angle (зеркало вокруг pivot'а); флип
                // корня (h_mirror) зеркалит весь локальный фрейм и поправки не требует.
                // angle == null — «как загружено»: мировой upright, поза предмета = его рисунок
                // (копьё, нарисованное вертикально, остаётся вертикальным; поворот точки игнорируется).
                if (a.angle.HasValue)
                {
                    float drawn = flip ? Mathf.Repeat(180f - m.variants[vi].angle, 360f) : m.variants[vi].angle;
                    m.go.transform.localEulerAngles = new Vector3(0f, 0f, a.angle.Value - drawn);
                }
                else
                    m.go.transform.rotation = Quaternion.identity;
                // a.scale — целевой МИРОВОЙ масштаб (как на земле). Точка (pt) живёт под нормализованной
                // Metadata-веткой скелета, её lossyScale.y = bodyScale носителя. Делим на него, иначе предмет
                // унаследовал бы это сжатие и в руке был бы в bodyScale раз мельче того же prefab'а на земле
                // (на giant — ещё мельче). Якорь не масштабируется (Point.localScale=1), root.y=1 → lossyScale.y чист.
                float body = Mathf.Abs(pt.lossyScale.y);
                if (body < 0.0001f) body = 1f;
                float s = a.scale / body;
                m.go.transform.localScale = new Vector3(s, s, 1f);

                // Глубина кожи якоря: base тот же, что у частей тела (SpriterDotNetBehaviour прокидывает
                // его в UnityAnimator). «Чуть выше» кожи — за счёт ZLift, +1 к order не делаем: ранги
                // плотные, +1 попал бы ровно на следующую часть (голову — поверх нагрудника и должна быть).
                m.sr.sortingOrder = a.z.HasValue ? _beh.SortingOrder + a.z.Value : FallbackOrder;
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
