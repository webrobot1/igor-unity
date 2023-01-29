using System;
using System.Collections;
using UnityEngine;

public class EnemyModel : ObjectModel
{
	/// <summary>
	/// проходимая дистанция за FixedUpdate (учитывается скорость игрока)
	/// </summary>
	public LifeModel lifeBar;


	public void SetData(EnemyRecive data)
	{

        if (data.components != null) 
		{
			if (data.components.hp != null)
			{
				lifeBar.hp = (int)data.components.hp;
			}
			if (data.components.hpMax != null)
				lifeBar.hpMax = (int)data.components.hpMax;

			if (data.components.mp != null)
				lifeBar.mp = (int)data.components.mp;

			// ниже сравниваем c null тк может быть значение 0 которое надо обработать
			if (data.components.mpMax != null)
				lifeBar.mpMax = (int)data.components.mpMax;
		}

		base.SetData(data);
	}
}
