using UnityEngine;

namespace Mmogick
{
    // Diablo-подобная подсветка подбираемых предметов, лежащих в мире: обводка по силуэту — дубликат
    // иконки предмета с outline-материалом (Mmogick/SpriteOutline), золотой пульсирующий контур.
    // Обводка — отдельный child (оригинальный спрайт, скелет и корневой SR НЕ трогаем). Внутри SortingGroup
    // корня уходит чуть позади тела (order < 0), но рисует только КРАЙ (внутри прозрачен), поэтому тело
    // не перекрывает.
    //
    // Имя предмета показывает не этот компонент, а общая для всех сущностей надпись под курсором
    // (HoverLabel): «тут лежит подбираемое» маркирует постоянная обводка, а имя — вопрос к конкретной
    // сущности, и адресуется курсором. Предмету надпись даётся своим префабом вида (PrefabItem).
    //
    // КОГО помечаем решает ВЫЗЫВАЮЩИЙ (MainController.UpdateObject через AnimationCacheService.IsGroundItem),
    // а не этот компонент: маркер вешается только на подбираемые предметы (kind=item / экипируемые),
    // поэтому Apply здесь уже без проверки критерия — он просто навешивает/обновляет подсветку.
    public class EquipableGroundMarker : MonoBehaviour
    {
        /// <summary>Префаб вида подписи для вещей на земле — тёплая золотистая, под цвет обводки.</summary>
        private const string PrefabItem = "Prefabs/UI/WorldLabelItem";

        private const string OutlineMaterialResource = "Materials/EquipableOutline";
        private const int OutlineSortingOrder = -1;     // чуть позади тела внутри SortingGroup корня
        private const float OutlineWidthMin = 2.5f;     // толщина контура (px текстуры), пульсирует
        private const float OutlineWidthMax = 4.5f;
        private const float OutlineAlphaMin = 0.6f;
        private const float OutlineAlphaMax = 1f;

        private const float PulseSpeed = 3f;            // темп мерцания контура

        // Шарим между всеми маркерами: материал-шаблон обводки.
        private static Material _outlineMaterial;

        private SpriteRenderer _outline;
        private MaterialPropertyBlock _outlineMpb;
        private static readonly int IdOutlineWidth = Shader.PropertyToID("_OutlineWidth");
        private static readonly int IdOutlineAlpha = Shader.PropertyToID("_Alpha");

        // Навесить/обновить подсветку. Критерий «это предмет» проверяет ВЫЗЫВАЮЩИЙ (MainController) — сюда
        // приходят только подбираемые предметы.
        public static void Apply(GameObject go)
        {
            var marker = go.GetComponent<EquipableGroundMarker>();
            if (marker == null) marker = go.AddComponent<EquipableGroundMarker>();

            marker.EnsureOutline();
            HoverLabel.Apply(go, PrefabItem);
        }

        // --- Обводка ---

        private void EnsureOutline()
        {
            // Без иконки на корневом SR обводить нечего (у сущности со скелетом корневой SR выключен; но
            // подбираемые предметы статичны-image, у них иконка на корневом SR). Легитимный fallback:
            // контур не строим.
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

        // Сносим обводку. Unity зовёт OnDestroy и при явном Destroy(этого компонента) — так
        // ObjectModel.Destroy() снимает подсветку в момент старта удаления предмета, чтобы она не висела
        // на «исчезающем» предмете во время remove-анимации (Puff ~пара секунд до Destroy(gameObject)) —
        // и при уничтожении самого предмета. Очистка инкапсулирована здесь: вызывающему достаточно
        // Destroy(marker). После Destroy компонента LateUpdate уже не зовётся — пульс сам встаёт.
        private void OnDestroy()
        {
            if (_outline != null) Destroy(_outline.gameObject);
        }

        private void LateUpdate()
        {
            // Обводка: пульс толщины + альфы через MaterialPropertyBlock (не плодим материалы).
            if (_outline != null && _outlineMpb != null)
            {
                float pulse = (Mathf.Sin(Time.time * PulseSpeed) + 1f) * 0.5f;
                _outline.GetPropertyBlock(_outlineMpb);
                _outlineMpb.SetFloat(IdOutlineWidth, Mathf.Lerp(OutlineWidthMin, OutlineWidthMax, pulse));
                _outlineMpb.SetFloat(IdOutlineAlpha, Mathf.Lerp(OutlineAlphaMin, OutlineAlphaMax, pulse));
                _outline.SetPropertyBlock(_outlineMpb);
            }
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
    }
}
