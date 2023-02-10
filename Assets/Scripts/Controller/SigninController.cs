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

        public override void Error(string error)
        {
            Debug.LogError(error);
            GameObject.Find("error").GetComponent<Text>().text = error;
        }
    }
}