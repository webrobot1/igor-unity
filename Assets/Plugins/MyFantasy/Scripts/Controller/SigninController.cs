using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
    public class SigninController : BaseController
    {
        [SerializeField]
        protected InputField loginField;

        [SerializeField]
        protected InputField passwordField;

        protected virtual void Start()
        {
           if (loginField == null)
               Error("не присвоен loginField для ввода логина");

            if (passwordField == null)
                Error("не присвоен passwordField дляввода пароля");
        }

        public void Register()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
        
            StartCoroutine(HttpRequest("register"));
        }

        public void Auth()
        {
            login = this.loginField.text;
            password = this.passwordField.text;

            StartCoroutine(HttpRequest("auth"));     
        }
    }
}
