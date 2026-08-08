using System.Collections.Generic;
using UnityEngine;
using SpriterDotNetUnity;

namespace Mmogick
{
    // Вешает внешние спрайты-предметы (оружие/щит) на Spriter-точки скелета сущности.
    // Носитель — ЛЮБАЯ сущность с экипировкой: свой игрок, чужой игрок, моб (компонент equip публичный,
    // приходит всем видящим). Точка входа — Sync: слот + prefab надетого предмета.
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

        // Доля роста НОСИТЕЛЯ, которую занимает надетый предмет по своей длинной стороне при scale слота = 1.
        // Тело любой сущности нормируется в одну клетку карты (NewSpriterRuntimeImporter.SpriterPostImportAdjuster,
        // TARGET_HEIGHT; клетка Grid = 1 мировой юнит), поэтому «доля клетки» и «доля роста носителя» — одно число.
        // Размер надетого предмета — величина, задаваемая ЗДЕСЬ, а не разрешением исходника: без нормализации
        // предмет получал бы размер своего PNG в масштабе скелета (400px-меч ≈ рост персонажа, 574px-посох —
        // полтора роста). Тонкая подгонка под конкретный слот/скелет — множитель scale слота (сервер).
        private const float MountSpan = 0.6f;

        // Один якорь слота: Spriter-точка + позиционирование предмета относительно неё.
        public class Anchor
        {
            public string pointName; // имя точки (object.name из object_slot)
            public float ox, oy;     // сдвиг от точки, px
            public float scale;      // ЧИСТЫЙ scale слота: множитель к MountSpan (доля роста носителя)
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
            public float span;       // длинная сторона видимых пикселей исходника, мировые юниты при scale=1
        }

        private class Mounted
        {
            public string slot;          // slug слота экипировки: по нему резолвятся якоря на скелете носителя
            public Anchor[] anchors;     // null — якорей нет ЛИБО структура носителя ещё не докачана (см. ResolveAnchors)
            public bool anchorsTried;
            public SpriterDotNetBehaviour anchorBeh;   // скелет, под который якоря резолвили: сменился — резолвим заново
            public Variant[] variants;   // ≥1, в серверном порядке (angle ASC); активный выбирается по ракурсу тела
            public string rotationMode;  // AnimationCacheService.RotationMode.* (mirror_x даёт зеркальных кандидатов)
            public GameObject go;
            public SpriteRenderer sr;
            public int curVariant = -1;  // кэш выбора: спрайт подменяется только при смене варианта/флипа
            public bool curFlip;
        }

        private readonly Dictionary<string, Mounted> _slots = new Dictionary<string, Mounted>();
        private SpriterDotNetBehaviour _beh;
        private EntityModel _em;   // DisplayAngle (ракурс играющего клипа) + Forward (fallback) — выбор варианта картинки

        // Надеть/снять предмет в слоте экипировки ЛЮБОЙ сущности: wearer — носитель, itemPrefab — prefab
        // надетого предмета (пусто = снять). Единая точка для своего игрока, чужих игроков и мобов:
        // компонент equip публичный и несёт prefab, инвентарь носителя тут не нужен.
        // Предмет-картинка (image-prefab) — единственный поддерживаемый визуал предмета.
        public static void Sync(EntityModel wearer, string slot, string itemPrefab)
        {
            if (wearer == null)
                return;

            WeaponMount mount = wearer.GetComponent<WeaponMount>();

            if (string.IsNullOrEmpty(itemPrefab))
            {
                if (mount != null) mount.Detach(slot);
                return;
            }

            List<AnimationCacheService.ImageVariant> variants = AnimationCacheService.GetPrefabImageVariants(itemPrefab);
            if (variants == null)
                return;   // не image-prefab: визуала предмета нет

            // Спрайты всех вариантов из локального кеша (битый файл — пропуск с warning, не валим экип).
            var sources = new List<VariantSource>();
            foreach (AnimationCacheService.ImageVariant v in variants)
            {
                Sprite s;
                try { s = AnimationCacheService.TryGetSprite(BaseController.GAME_ID, v.File); }
                catch (System.Exception ex) { Debug.LogWarning("WeaponMount " + itemPrefab + " вариант " + v.angle + "°: " + ex.Message); continue; }
                if (s == null) continue;
                sources.Add(new VariantSource { angle = v.angle, sprite = s, pivotX = v.pivotX, pivotY = v.pivotY });
            }
            if (sources.Count == 0)
                return;   // ни один вариант не загрузился

            if (mount == null) mount = wearer.gameObject.AddComponent<WeaponMount>();
            mount.Apply(slot, sources.ToArray(), AnimationCacheService.GetPrefabRotationMode(itemPrefab));
        }

        // Снять всё надетое (сервер прислал пустую экипировку — full-clear).
        public static void DetachAll(EntityModel wearer)
        {
            WeaponMount mount = wearer != null ? wearer.GetComponent<WeaponMount>() : null;
            if (mount == null)
                return;

            foreach (string slot in new List<string>(mount._slots.Keys))
                mount.Detach(slot);
        }

        // Надеть/обновить предмет в слоте: variants — все варианты картинки по направлениям (активный
        // выбирается по ракурсу тела в LateUpdate), rotationMode — AnimationCacheService.RotationMode.*
        // (mirror_x добавляет зеркальных кандидатов). Якоря слота резолвятся отложенно (ResolveAnchors).
        // Grip-спрайты (pivot = хват, 0..1, центр вращения) пересоздаются из текстур исходников один раз
        // здесь — не в LateUpdate (Sprite.Create аллоцирует). Здесь же замеряется span варианта: у grip-спрайта
        // меш FullRect (прозрачные поля включены), поэтому видимые пиксели меряем по ИСХОДНИКУ из кеша — он
        // создан с мешом Tight, и tight-rect у него честный (тем же замером нормализуется предмет на земле).
        // Масштаб под span и bodyScale носителя считает LateUpdate — он зависит от активного якоря и варианта.
        private void Apply(string slot, VariantSource[] variants, string rotationMode)
        {
            Detach(slot);   // пересоздаём с нуля: освобождает прежние grip-спрайты (набор мог измениться)
            if (variants == null || variants.Length == 0)
                return;

            Mounted m = new Mounted { slot = slot, go = new GameObject("Weapon_" + slot) };
            m.sr = m.go.AddComponent<SpriteRenderer>();
            m.rotationMode = rotationMode;
            m.variants = new Variant[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                Texture2D tex = variants[i].sprite.texture;
                Vector2 visible = AnimationCacheService.TryGetTightRect(variants[i].sprite, out Rect tight)
                    ? tight.size
                    : variants[i].sprite.bounds.size;
                m.variants[i] = new Variant
                {
                    angle = variants[i].angle,
                    grip = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(variants[i].pivotX, variants[i].pivotY), Ppu, 0, SpriteMeshType.FullRect),
                    span = Mathf.Max(visible.x, visible.y),
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

        // Сущность уничтожается (умерла, ушла из зоны видимости, мир перезагрузился) — вместе с ней
        // уходит дочерний объект предмета, но grip-спрайты созданы через Sprite.Create и живут отдельно
        // от сцены: без явного Destroy они копятся в памяти на каждом снятом с карты существе.
        private void OnDestroy()
        {
            foreach (Mounted m in _slots.Values)
                if (m.variants != null)
                    foreach (Variant v in m.variants)
                        if (v != null && v.grip != null) Destroy(v.grip);

            _slots.Clear();
        }

        // Якоря слота лежат в структуре скелета НОСИТЕЛЯ и попадают в кеш вместе с ней. Структура
        // качается асинхронно (UpdateController.ApplyVisualPrefab), а экипировка приезжает тем же
        // пакетом, что и сама сущность, — на момент Apply кеша ещё может не быть. Потому резолв
        // отложенный: повтор — при появлении/смене скелета (к этому моменту структура уже на диске),
        // не каждый кадр, чтение sidecar с диска дорогое.
        // z — draw-rank кожи кости якоря, на нём LateUpdate строит sortingOrder предмета; scale — множитель
        // к MountSpan: размер предмета в руке задаётся долей роста НОСИТЕЛЯ, а scale слота подгоняет её под
        // конкретный слот/скелет. size привязки в руке не участвует — он задаёт размер предмета только
        // на земле и в инвентаре (UpdateController.ApplyVisualPrefab): высота лежащего предмета и его
        // размер в руке — две независимые величины, общего множителя у них нет.
        private Anchor[] ResolveAnchors(string slot)
        {
            string wearerPrefab = _em != null ? _em.prefab : null;
            if (string.IsNullOrEmpty(wearerPrefab))
                return null;

            List<AnimationCacheService.ObjectSlotEntry> entries =
                AnimationCacheService.GetSlotEntries(BaseController.GAME_ID, wearerPrefab, slot);
            if (entries == null)
                return null;   // структура ещё не докачана либо у скелета нет якорей этого слота

            var anchors = new List<Anchor>();
            foreach (AnimationCacheService.ObjectSlotEntry entry in entries)
            {
                if (entry == null || entry.anchor == null || entry.anchor.type != "point")
                    continue;   // якорь без точки (кость сервер подменяет на «<bone>_point» сам)
                anchors.Add(new Anchor
                {
                    pointName = entry.anchor.name,
                    ox        = entry.offsetX,
                    oy        = entry.offsetY,
                    angle     = entry.angle,
                    scale     = entry.scale,
                    z         = entry.z,
                });
            }
            return anchors.Count > 0 ? anchors.ToArray() : null;
        }

        // Выбор варианта картинки под экранный ракурс тела (fwdDeg). Экранный угол кандидата учитывает
        // зеркало ТЕЛА (флип корня h_mirror — предмет-потомок зеркалится вместе с телом, второй раз
        // зеркалить нельзя) и собственный flipX предмета (только mirror_x: лево из права):
        // screen = (mirrored XOR flip) ? 180−angle : angle.
        // Побеждает кандидат с минимальной |DeltaAngle(fwdDeg, screen)|; для none/free flip-кандидатов нет.
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

                // Якоря — по скелету носителя; пока его нет (структура качается) либо он сменился
                // (смена prefab на лету), пробуем отрезолвить заново. См. ResolveAnchors.
                if (m.anchors == null && (!m.anchorsTried || !ReferenceEquals(m.anchorBeh, _beh)))
                {
                    m.anchors = ResolveAnchors(m.slot);
                    m.anchorsTried = true;
                    m.anchorBeh = _beh;
                }
                if (m.anchors == null) { m.go.SetActive(false); continue; }   // якорей этого слота у носителя нет

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

                // Вариант картинки — под ФАКТИЧЕСКИЙ ракурс тела, не под логический forward: ракурсов
                // может быть меньше, чем направлений (GetClipName «прилипает» к ближайшему клипу), и у
                // существа с единственным фронтальным видом тело смотрит вниз при любом forward — предмет
                // обязан следовать за телом. DisplayAngle — нарисованный угол играющего клипа; экранный
                // ракурс = 180−angle при зеркале корня (mirrored — знак мирового X-масштаба точки включает
                // все родительские флипы). Forward — fallback: клип без направления / резолв не удался.
                bool mirrored = pt.lossyScale.x < 0f;
                float fwdDeg;
                int? bodyAngle = _em != null ? _em.DisplayAngle : null;
                if (bodyAngle.HasValue)
                    fwdDeg = mirrored ? Mathf.Repeat(180f - bodyAngle.Value, 360f) : bodyAngle.Value;
                else
                {
                    Vector3 fwd = _em != null ? _em.Forward : Vector3.right;
                    if (fwd.x == 0f && fwd.y == 0f) fwd = Vector3.right;
                    fwdDeg = Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg;
                }
                PickVariant(m, fwdDeg, mirrored, out int vi, out bool flip);
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
                // Размер в руке НОРМИРУЕМ: длинная сторона видимых пикселей предмета = MountSpan × scale слота
                // (доля роста носителя). Предмет живёт child'ом якоря и унаследовал бы масштаб скелета вместе
                // с разрешением своего PNG — размер получался бы не из данных, а из того, в каком разрешении
                // художник нарисовал файл. Потому делим на bodyScale: точка (pt) сидит под нормированной
                // Metadata-веткой, её lossyScale.y = bodyScale носителя, и после деления мировой размер
                // предмета зависит только от MountSpan и scale слота — одинаково у любого носителя.
                // size предмета сюда не входит: высота на земле задаётся сервером отдельно и в руке не
                // участвует (UpdateController.ApplyVisualPrefab).
                float body = Mathf.Abs(pt.lossyScale.y);
                if (body < 0.0001f) body = 1f;
                float span = m.variants[vi].span;
                float s = span > 0.0001f ? MountSpan * a.scale / (span * body) : a.scale;
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
