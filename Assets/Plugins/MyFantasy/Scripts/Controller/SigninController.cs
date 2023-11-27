using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
    public class SigninController : BaseController
    {
        [SerializeField]
        protected Text loginField;

        [SerializeField]
        protected InputField passwordField;        
        
        [SerializeField]
        protected Text gameIdField;      // ����� ������ ���� ������ id ������ ������� � ������ �������� http://my-fantasy.ru/  ������� ����

        protected virtual void Start()
        {
           if (loginField == null)
               Error("�� �������� loginField ��� ����� ������");

            if (passwordField == null)
                Error("�� �������� passwordField ���� ���� ������");     
            
            if (gameIdField == null)
                Error("�� �������� gameIdField ��� �������������� � ����� �� ���� �������� http://my-fantasy.ru/ � ������������� ����� ������");
        }

        public void Register()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
            game_id = this.gameIdField.text;
        
            StartCoroutine(HttpRequest("register"));
        }

        public void Auth()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
            game_id = this.gameIdField.text;

            StartCoroutine(HttpRequest("auth"));     
        }
    }
}
