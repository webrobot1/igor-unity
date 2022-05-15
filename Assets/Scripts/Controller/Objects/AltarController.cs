using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//when something get into the alta, make the runes glow
public class AltarController : MonoBehaviour
{
    public List<SpriteRenderer> runes;
    public float lerpSpeed;

    private Color curColor;
    private Color targetColor;

    private PlayerModel player;


    private void OnTriggerStay2D(Collider2D other)
    {
        if ((player = other.GetComponent<PlayerModel>()) && player.lifeBar.hp == 0)
        {
            targetColor = new Color(1, 1, 1, 1);
            ConnectController.player.resurrect();
        }

        curColor = Color.Lerp(curColor, targetColor, lerpSpeed * Time.deltaTime);
        foreach (var r in runes)
        {
            r.color = curColor;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        targetColor = new Color(1, 1, 1, 0);
    }
}
