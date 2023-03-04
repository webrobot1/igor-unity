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
               Error("�� �������� loginField ��� ����� ������");

            if (passwordField == null)
                Error("�� �������� passwordField �������� ������");
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
