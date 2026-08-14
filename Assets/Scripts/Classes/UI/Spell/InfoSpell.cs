using Newtonsoft.Json.Linq;

namespace Mmogick
{
    /// <summary>
    /// Иконка заклинания в окне сведений о существе: показывает, что оно умеет. Картинка, подсказка по
    /// наведению и запрет применения — общие для перечисляемого окном (<see cref="InfoIcon"/>).
    ///
    /// Отличие от <see cref="Spell"/> из книги: там свои заклинания, которые применяют и раскладывают
    /// по быстрым клавишам, здесь — чужие умения, и трогать их нельзя.
    /// </summary>
    public class InfoSpell : InfoIcon
    {
        /// <summary>
        /// К названию и описанию добавляем то, чем заклинание отличается от прочего показанного в
        /// окне, — во что оно обходится. Стоимость лежит свойством самого заклинания в каталоге:
        /// отдельного справочника заклинаний сервер не шлёт, книга собирается оттуда же.
        /// </summary>
        public override string GetTooltipText()
        {
            string text = base.GetTooltipText();
            JToken cost = AnimationCacheService.GetComponentValue(_prefab, SpellBookController.COMPONENT_MP_COST, null);

            if (cost != null)
                text += "\n" + TextStyle.Value("Мана: " + cost.Value<int>());

            return text;
        }
    }
}
