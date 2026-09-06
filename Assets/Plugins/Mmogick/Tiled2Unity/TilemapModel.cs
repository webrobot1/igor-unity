using System.Collections.Generic;

namespace UnityEngine.Tilemaps
{
    public class TilemapModel : Tile
    {
        private List<Sprite> sprites = new List<Sprite> { };
        protected TilemapModel() { }

        /// <summary>
        /// Скорость показа кадров плитки, кадров в секунду. Ею же задана ЦЕНА одного кадра списка
        /// (<see cref="FrameMs"/>): длительность кадра приходит с сервера в миллисекундах, а список
        /// набирается повторами спрайта — по одному повтору на кадр показа. Величины связаны, врозь их
        /// менять нельзя: сменишь скорость, не сменив шага, — длительность кадра плитки уедет.
        /// </summary>
        private const int Fps = 100;

        /// <summary>Сколько миллисекунд длится один кадр показа при <see cref="Fps"/>.</summary>
        private const int FrameMs = 1000 / Fps;

        public override bool GetTileAnimationData(Vector3Int location, ITilemap tileMap, ref TileAnimationData tileAnimationData)
        {
            if (sprites != null && sprites.Count > 0)
            {
                tileAnimationData.animatedSprites = sprites.ToArray();
                tileAnimationData.animationSpeed = Fps;
                tileAnimationData.animationStartTime = 0;
                return true;
            }
            return false;
        }

        public void addSprites(Mmogick.TileAnimation[] animations)
        {
            foreach (Mmogick.TileAnimation anim in animations)
            {
                for (int i = 0; i < anim.duration; i += FrameMs)
                {
                    this.sprites.Add(anim.sprite);
                }

                // Первый кадр идёт и в обычный спрайт тайла: анимацию тайл-карта крутит сама, но ДО её первого
                // шага (и в любом статичном показе — снимок карты одним кадром, редактор) рисуется именно он.
                // Без него анимированный тайл в таком показе пуст: вода и прочие анимированные поверхности
                // проваливаются дырой, хотя в игре видны.
                if (this.sprite == null)
                    this.sprite = anim.sprite;
            }
        }
    }
}
