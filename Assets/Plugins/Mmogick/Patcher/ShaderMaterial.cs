using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mmogick
{
	/// <summary>
	/// Материал по имени шейдера — ОДИН на весь сеанс. Материал поверх шейдера от места установки не зависит,
	/// а созданный кодом объект движка сам не исчезает: свой у каждой сборки визуала копился бы сиротами до
	/// конца сеанса. Материал общий — кому нужен свой (своя текстура на кусок), тот спрашивает шейдер сам.
	///
	/// Выдача проверяет живость Unity-оператором и при промахе делает материал заново: остановка игры
	/// уничтожает созданное кодом, а статика её переживает (перезагрузка домена в проекте выключена).
	/// </summary>
	public static class ShaderMaterial
	{
		private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

		/// <summary>
		/// Материал по имени шейдера. Шейдер — часть сборки клиента, и его отсутствие значит сборку битую:
		/// падаем, а не рисуем подставленным дефолтом. purpose называет, что без шейдера не нарисуется —
		/// одно его имя читателю журнала места поломки не даёт.
		/// </summary>
		public static Material Get(string shader, string purpose)
		{
			if (_materials.TryGetValue(shader, out Material known) && known != null)
				return known;

			Shader found = Shader.Find(shader);
			if (found == null)
				throw new InvalidOperationException("В сборке клиента нет шейдера «" + shader + "» — " + purpose);

			Material material = new Material(found);
			_materials[shader] = material;
			return material;
		}
	}
}
