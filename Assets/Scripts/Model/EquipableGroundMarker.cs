using UnityEngine;

namespace Mmogick
{
    // Diablo-подобная подсветка подбираемых предметов, лежащих в мире. Два слоя поверх визуала предмета,
    // оба — отдельные child'ы (оригинальный спрайт/Spriter/корневой SR НЕ трогаем):
    //   1) Обводка по силуэту — дубликат иконки предмета с outline-материалом (Mmogick/SpriteOutline),
    //      золотой пульсирующий контур. Внутри SortingGroup корня уходит чуть позади тела (order < 0),
    //      но рисует только КРАЙ (внутри прозрачен), поэтому тело не перекрывает.
    //   2) Надпись с именем — полупрозрачная плашка (TextMesh + фон) над предметом, всегда видна
    //      приглушённо, ярче при наведении курсора. Кликабельна: у неё свой BoxCollider2D, и клик
    //      по ней CursorController трактует как клик по родителю-предмету (GetComponentInParent<EntityModel>)
    //      → подход к предмету → сервер подбирает. Отдельной pickup-команды нет (см. CursorController).
    //
    // КОГО помечаем решает ВЫЗЫВАЮЩИЙ (MainController.UpdateObject через AnimationCacheService.IsGroundItem),
    // а не этот компонент: маркер вешается только на подбираемые предметы (kind=item / экипируемые),
    // поэтому Apply здесь уже без проверки критерия — он просто навешивает/обновляет подсветку.
    //
    // РАЗМЕР НАДПИСИ универсален и не зависит от предмета: фиксированный characterSize задаёт высоту в
    // ЛОКАЛЬНЫХ единицах надписи, а разовая компенсация scale корня (label.localScale = 1/rootScale)
    // приводит локальные единицы к клеткам мира — итоговая мировая высота строки одинакова для любого
    // предмета, без покадровой подгонки под bounds. scale корня детерминирован (нормализация по серверному
    // size в UpdateController.ApplyVisualPrefab), поэтому компенсацию достаточно посчитать один раз.
    public class EquipableGroundMarker : MonoBehaviour
    {
        // --- Обводка ---
        private const string OutlineMaterialResource = "Materials/EquipableOutline";
        private const int OutlineSortingOrder = -1;     // чуть позади тела внутри SortingGroup корня
        private const float OutlineWidthMin = 2.5f;     // толщина контура (px текстуры), пульсирует
        private const float OutlineWidthMax = 4.5f;
        private const float OutlineAlphaMin = 0.6f;
        private const float OutlineAlphaMax = 1f;

        // --- Надпись ---
        private static readonly Color LabelTextColor = new Color(1f, 0.95f, 0.7f, 1f);   // тёплый светлый
        private static readonly Color LabelBgColor = new Color(0f, 0f, 0f, 1f);          // тёмная плашка (альфа ниже)
        // characterSize подобран под fontSize=64 + LegacyRuntime.ttf так, чтобы при компенсации scale корня
        // (label.lossyScale == 1, т.е. локальные единицы = клетки) мировая высота строки была ~0.28 клетки
        // (≤ предмета, который нормализован к ~1 клетке). Меняешь fontSize/шрифт — пересчитай это число.
        private const int LabelFontSize = 64;
        private const float LabelCharSize = 0.04f;
        private const float LabelWorldOffsetY = 0.62f;  // подъём центра надписи над центром предмета (в клетках)
        private const int LabelBgOrder = 60;            // поверх тела и контура
        private const int LabelTextOrder = 61;          // поверх фона
        private const float LabelPadX = 0.12f;          // отступ фона вокруг текста (в клетках)
        private const float LabelPadY = 0.06f;
        private const float LabelTextAlphaIdle = 0.55f;
        private const float LabelTextAlphaHover = 1f;
        private const float LabelBgAlphaIdle = 0.45f;
        private const float LabelBgAlphaHover = 0.78f;
        private const float LabelFadeSpeed = 10f;       // скорость перехода idle↔hover

        private const float PulseSpeed = 3f;            // темп мерцания контура

        // Шарим между всеми маркерами: материал-шаблон обводки, шрифт, фон-спрайт, камера.
        private static Material _outlineMaterial;
        private static Font _labelFont;
        private static Sprite _bgSprite;
        private static Camera _cam;

        // Обводка
        private SpriteRenderer _outline;
        private MaterialPropertyBlock _outlineMpb;
        private static readonly int IdOutlineWidth = Shader.PropertyToID("_OutlineWidth");
        private static readonly int IdOutlineAlpha = Shader.PropertyToID("_Alpha");

        // Надпись
        private Transform _label;
        private TextMesh _text;
        private MeshRenderer _textRenderer;
        private SpriteRenderer _bg;
        private BoxCollider2D _labelCollider;
        private bool _needLayout;   // одноразовая раскладка надписи (ждём готовности меша TextMesh)
        private float _hover;       // 0..1, сглаженная «яркость» наведения

        // Навесить/обновить подсветку. Критерий «это предмет» проверяет ВЫЗЫВАЮЩИЙ (MainController) — сюда
        // приходят только подбираемые предметы. prefab непуст только в полном пакете спавна / при смене prefab.
        public static void Apply(GameObject go, string prefab)
        {
            var marker = go.GetComponent<EquipableGroundMarker>();
            if (marker == null) marker = go.AddComponent<EquipableGroundMarker>();
            marker.SetItem(CleanItemName(AnimationCacheService.GetPrefabName(prefab) ?? prefab));
        }

        private void SetItem(string itemName)
        {
            EnsureOutline();
            EnsureLabel();
            if (_text != null && _text.text != itemName)
            {
                _text.text = itemName;
                _needLayout = true;   // имя/размер изменились — пересчитать раскладку один раз
            }
        }

        // --- Обводка ---

        private void EnsureOutline()
        {
            // Без иконки на корневом SR обводить нечего (Spriter-предметы — корневой SR выключен; но подбираемые
            // предметы статичны-image, у них иконка на корневом SR). Легитимный fallback: контур не строим.
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr == null || rootSr.sprite == null)
                return;

            if (_outline == null)
            {
                var child = new GameObject("EquipableOutline");
                child.transform.SetParent(transform, false);
                child.transform.localPosition = Vector3.zero;
                child.transform.localScale = Vector3.one;   // 1:1 с корневым SR (контур считается в texel-space)
                child.layer = gameObject.layer;

                _outline = child.AddComponent<SpriteRenderer>();
                _outline.sharedMaterial = GetOutlineMaterial();
                _outline.sortingOrder = OutlineSortingOrder;
                _outline.sortingLayerID = rootSr.sortingLayerID;
                _outlineMpb = new MaterialPropertyBlock();
            }

            // Спрайт обводки = текущая иконка предмета (обновляется при смене prefab — Apply зовётся заново).
            _outline.sprite = rootSr.sprite;
        }

        // --- Надпись ---

        private void EnsureLabel()
        {
            if (_label != null) return;

            var labelGo = new GameObject("EquipableLabel");
            labelGo.transform.SetParent(transform, false);
            labelGo.layer = gameObject.layer;
            _label = labelGo.transform;

            var rootSr = GetComponent<SpriteRenderer>();

            // Фон-плашка (полупрозрачный прямоугольник под текстом).
            var bgGo = new GameObject("Bg");
            bgGo.transform.SetParent(_label, false);
            bgGo.layer = gameObject.layer;
            _bg = bgGo.AddComponent<SpriteRenderer>();
            _bg.sprite = GetBgSprite();
            _bg.color = new Color(LabelBgColor.r, LabelBgColor.g, LabelBgColor.b, LabelBgAlphaIdle);
            _bg.sortingOrder = LabelBgOrder;
            if (rootSr != null) _bg.sortingLayerID = rootSr.sortingLayerID;

            // Текст.
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_label, false);
            textGo.layer = gameObject.layer;
            _text = textGo.AddComponent<TextMesh>();
            _text.font = GetLabelFont();
            _text.characterSize = LabelCharSize;
            _text.fontSize = LabelFontSize;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.color = new Color(LabelTextColor.r, LabelTextColor.g, LabelTextColor.b, LabelTextAlphaIdle);
            _textRenderer = textGo.GetComponent<MeshRenderer>();
            _textRenderer.sharedMaterial = GetLabelFont().material;   // material шрифта → рисуется поверх фона
            _textRenderer.sortingOrder = LabelTextOrder;
            if (rootSr != null) _textRenderer.sortingLayerID = rootSr.sortingLayerID;

            // Кликабельная зона надписи (клик → CursorController поднимется к родителю-предмету и подберёт).
            _labelCollider = labelGo.AddComponent<BoxCollider2D>();
            _labelCollider.isTrigger = true;   // не участвует в физике, только в picking/raycast

            _needLayout = true;
        }

        // Разовая раскладка надписи: компенсация scale корня + позиция + подгонка фона/collider'а под текст.
        // Зовётся один раз после готовности меша TextMesh (не покадрово) — scale предмета не меняется.
        private void LayoutLabel()
        {
            Vector3 rs = transform.localScale;
            float ay = Mathf.Abs(rs.y) > 1e-4f ? Mathf.Abs(rs.y) : 1f;
            // X делим со знаком: если корень mirror-flip'нут (rs.x<0), компенсируем, чтобы текст не зеркалился.
            float sx = Mathf.Abs(rs.x) > 1e-4f ? rs.x : 1f;
            // Компенсация: локальные единицы надписи = клетки мира (lossyScale ≈ 1) для любого предмета.
            _label.localScale = new Vector3(1f / sx, 1f / ay, 1f);
            _label.localPosition = new Vector3(0f, LabelWorldOffsetY / ay, 0f);

            // Фон и collider под фактический размер текста (в локальных единицах label).
            Vector3 ls = _label.lossyScale;
            Vector3 tb = _textRenderer.bounds.size;
            float w = (Mathf.Abs(ls.x) > 1e-4f ? tb.x / Mathf.Abs(ls.x) : tb.x) + LabelPadX;
            float h = (Mathf.Abs(ls.y) > 1e-4f ? tb.y / Mathf.Abs(ls.y) : tb.y) + LabelPadY;
            _bg.transform.localScale = new Vector3(w, h, 1f);
            _labelCollider.size = new Vector2(w, h);
            _labelCollider.offset = Vector2.zero;
        }

        // Сносим обводку+надпись. Unity зовёт OnDestroy и при явном Destroy(этого компонента) — так
        // ObjectModel.Destroy() снимает подсветку в момент старта удаления предмета, чтобы она не висела
        // на «исчезающем» предмете во время remove-анимации (Puff ~пара секунд до Destroy(gameObject)) —
        // и при уничтожении самого предмета. Очистка инкапсулирована здесь: вызывающему достаточно
        // Destroy(marker). После Destroy компонента LateUpdate уже не зовётся — пульс/hover сами встают.
        private void OnDestroy()
        {
            if (_outline != null) Destroy(_outline.gameObject);
            if (_label != null) Destroy(_label.gameObject);
        }

        private void LateUpdate()
        {
            // 1) Обводка: пульс толщины + альфы через MaterialPropertyBlock (не плодим материалы).
            if (_outline != null && _outlineMpb != null)
            {
                float pulse = (Mathf.Sin(Time.time * PulseSpeed) + 1f) * 0.5f;
                _outline.GetPropertyBlock(_outlineMpb);
                _outlineMpb.SetFloat(IdOutlineWidth, Mathf.Lerp(OutlineWidthMin, OutlineWidthMax, pulse));
                _outlineMpb.SetFloat(IdOutlineAlpha, Mathf.Lerp(OutlineAlphaMin, OutlineAlphaMax, pulse));
                _outline.SetPropertyBlock(_outlineMpb);
            }

            if (_label == null) return;

            // 2) Разовая раскладка надписи — дождавшись, пока TextMesh сгенерит меш (bounds станет ненулевым).
            if (_needLayout && _textRenderer != null && _textRenderer.bounds.size.y > 1e-4f)
            {
                LayoutLabel();
                _needLayout = false;
            }

            // 3) Hover: курсор над плашкой → ярче (плавно). Только интерактив — не layout.
            bool hovered = false;
            if (_labelCollider != null)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null)
                {
                    Vector3 m = _cam.ScreenToWorldPoint(Input.mousePosition);
                    hovered = _labelCollider.OverlapPoint(new Vector2(m.x, m.y));
                }
            }
            _hover = Mathf.MoveTowards(_hover, hovered ? 1f : 0f, Time.deltaTime * LabelFadeSpeed);

            if (_text != null)
            {
                var c = LabelTextColor;
                c.a = Mathf.Lerp(LabelTextAlphaIdle, LabelTextAlphaHover, _hover);
                _text.color = c;
            }
            if (_bg != null)
            {
                var c = LabelBgColor;
                c.a = Mathf.Lerp(LabelBgAlphaIdle, LabelBgAlphaHover, _hover);
                _bg.color = c;
            }
        }

        // Имя для надписи: name из админки — это filename ("iron_sword.png"). Убираем расширение и
        // подчёркивания → пробелы для читаемости (Diablo показывает «iron sword», не «iron_sword.png»).
        private static string CleanItemName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            int dot = raw.LastIndexOf('.');
            if (dot > 0) raw = raw.Substring(0, dot);
            return raw.Replace('_', ' ');
        }

        // --- Шаренные ресурсы ---

        private static Material GetOutlineMaterial()
        {
            if (_outlineMaterial == null)
            {
                _outlineMaterial = Resources.Load<Material>(OutlineMaterialResource);
                if (_outlineMaterial == null)
                    throw new System.InvalidOperationException(
                        "EquipableGroundMarker: не найден материал Resources/" + OutlineMaterialResource +
                        ".mat (shader Mmogick/SpriteOutline). Создайте материал в Assets/Resources/Materials/.");
            }
            return _outlineMaterial;
        }

        private static Font GetLabelFont()
        {
            if (_labelFont == null)
                _labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _labelFont;
        }

        // Белый 1x1 спрайт для фона-плашки (цвет/прозрачность задаём через SpriteRenderer.color).
        private static Sprite GetBgSprite()
        {
            if (_bgSprite != null) return _bgSprite;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "EquipableLabelBg" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            // pixelsPerUnit = 1 → спрайт 1x1 = 1 мировой юнит при scale 1 (масштабируем в LayoutLabel).
            _bgSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            return _bgSprite;
        }
    }
}
