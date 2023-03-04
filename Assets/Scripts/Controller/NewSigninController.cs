using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
    public class NewSigninController : SigninController
    {
        protected override void Start()
        {
            Screen.orientation = ScreenOrientation.AutoRotation;
            base.Start();
        }
    }
}
