using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Кресты НА ЗЕМЛЕ в проходах, ведущих в недоступные локации (<see cref="MapController.getGates"/>).
    /// Там игрок упирается в невидимую стену: соседняя карта не поднята, её клетки сервер считает
    /// непроходимыми, а на земле нет ни стены, ни иного признака — крест и есть единственное объяснение,
    /// почему дальше не пройти. Виден он всем, кто рядом: это метка МЕСТА, а не чья-то личная.
    ///
    /// Метки стоят по ВСЕЙ ширине прохода, по кресту на клетку: упереться игрок может в любом его месте,
    /// а одна метка в середине широкого прохода оставила бы края необъяснёнными. Радар и обзорная карта
    /// мира берут у того же прохода только середину — там на клетку места нет (<see cref="MinimapController"/>).
    /// Рисунок креста один на все три показа (<see cref="MinimapController.UnavailableSprite"/>).
    ///
    /// Пересобираются метки не каждый кадр, а по смене самого набора проходов: доступность соседа сервер
    /// шлёт когда угодно, а между сменами метки неподвижны.
    /// </summary>
    abstract public class GateController : MinimapController
    {
        /// <summary>
        /// Имя слоя меток внутри карты — тем же порядком, что у слоя переходов (<see cref="WarpMarker.LAYER"/>):
        /// метки лежат в карте, потому уходят вместе с ней, когда игрок от неё удалился.
        /// </summary>
        private const string LAYER = "Gates";

        /// <summary>Сторона креста в клетках карты: метка объясняет клетку, в которую упёрлись, и занимает её.</summary>
        private const float SIZE = 1f;

        /// <summary>
        /// Непрозрачность метки НА ЗЕМЛЕ. Крестов тут подряд столько, сколько клеток в проходе, и в полную
        /// силу они складываются в плотную ленту, под которой не видно самой земли — а показать нужно МЕСТО,
        /// не закрасить его. На радаре и обзорной карте метка одна и мелкая, там гасить нечего.
        /// </summary>
        private const float ALPHA = 0.5f;

        /// <summary>
        /// Проходы, по которым выложены метки. Сравнивается ЭКЗЕМПЛЯР списка: getGates отдаёт готовый и
        /// пересобирает его только при смене входов — иной экземпляр и значит, что метки устарели.
        /// </summary>
        private List<Gate> _drawn;

        protected override void Update()
        {
            base.Update();

            List<Gate> gates = getGates();

            if (ReferenceEquals(gates, _drawn))
                return;

            _drawn = gates;
            Rebuild(gates);
        }

        /// <summary>
        /// Выкладывает метки заново по всем картам сцены. Заново — целиком: набор проходов сменился, и старые
        /// метки к нему отношения не имеют.
        ///
        /// Карта прохода на сцене ещё не построена (её графика приходит отдельным запросом) — метки этого
        /// прохода ждут: приход разметки сбрасывает счёт проходов, и выкладка повторится уже с картой.
        /// </summary>
        private void Rebuild(List<Gate> gates)
        {
            foreach (Transform grid in mapObject.transform)
            {
                Transform old = grid.Find(LAYER);
                if (old != null)
                    DestroyImmediate(old.gameObject);
            }

            Sprite sprite = UnavailableSprite();

            foreach (Gate gate in gates)
            {
                Transform grid = mapObject.transform.Find(gate.map.ToString());
                if (grid == null)
                    continue;

                Transform layer = grid.Find(LAYER);
                if (layer == null)
                {
                    layer = new GameObject(LAYER).transform;
                    layer.SetParent(grid, false);
                }

                // Порядок отрисовки — слой-земля карты (spawn_sort), тот же, что у существ и у меток
                // переходов: крест лежит с ними в одной плоскости, а не поверх крыш. Выше нельзя — закрывал
                // бы существ, ниже — утонул бы в тайлах земли, а между ними свободного значения нет:
                // слой-земля и существа стоят на одном порядке.
                // Карта в getMaps() и её объект на сцене появляются и уходят вместе — grid найден, значит есть и она.
                int order = getMaps()[gate.map].spawn_sort;

                // Клетки прохода: от края до края, шагом в клетку. Проход шириной в клетку даёт одну метку.
                int cells = Mathf.RoundToInt(Mathf.Abs(gate.to.x - gate.from.x) + Mathf.Abs(gate.to.y - gate.from.y)) + 1;
                Vector2 step = cells > 1 ? (gate.to - gate.from) / (cells - 1) : Vector2.zero;

                for (int i = 0; i < cells; i++)
                    Create(layer, sprite, gate.from + step * i, order);
            }
        }

        /// <summary>
        /// Крест на клетке. Место задаём МИРОВОЙ позицией: проходы считаны в координатах сцены (в них же
        /// стоят сущности), а слой лежит внутри карты, у которой свой сдвиг тайлов — местная позиция
        /// потребовала бы его вычитать, да ещё и с анкером клеток Tilemap'а.
        /// </summary>
        private static void Create(Transform layer, Sprite sprite, Vector2 at, int order)
        {
            GameObject go = new GameObject("gate");
            go.transform.SetParent(layer, false);
            go.transform.position = at;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;

            // Гасим ОТРИСОВКУ, а не сам рисунок: картинка креста одна на все три показа, и прозрачность,
            // положенная в её точки, приглушила бы заодно метки радара и обзорной карты.
            renderer.color = new Color(1f, 1f, 1f, ALPHA);

            // Размер картинки в мировых единицах задаёт её создание (точки на единицу) — считаем от него,
            // а не от числа точек рисунка.
            Vector3 size = sprite.bounds.size;
            if (size.x > 0f && size.y > 0f)
                go.transform.localScale = new Vector3(SIZE / size.x, SIZE / size.y, 1f);
        }
    }
}
