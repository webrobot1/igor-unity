using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MyFantasy
{
    public class EventRecive
    {
        public string action = null;

        /// <summary>
        /// ������� �������� �� �������� ������ .� �� �������� ������ ����� �� 100% �� ������� � ������� ����� �� �������� � ����� ���������� ���� ������. ������������ ��� ������ ����� ���������� ����� ����� 
        /// </summary>
        public double? remain = null;

        /// <summary>
        /// ������� ����� ������� �������. �������� ��� �������� ��� ���� � �������� ���� ��������� �� ������ �������. �� �� �������� ping (���� ����������� ������� ��� ����� ����������� ��� �������� ������� ������ ������) ��� ��� ���������� ��� ��������
        /// </summary>
        public double? timeout = null;

        public JObject data;

        public bool? from_client = null;          // ���� ��� ������� ��������� ��. ���� false �� ��� ��������� �� �������� ���������� �������� ������ (���� ��� ��������) ���� ����� ��������
    }
}
