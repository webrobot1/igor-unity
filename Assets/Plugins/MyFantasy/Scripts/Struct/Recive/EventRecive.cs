using System;
using System.Collections.Generic;

namespace MyFantasy
{
    public class EventRecive
    {
        public string action = null;

        /// <summary>
        /// ������� �������� �� �������� ������ .� �� �������� ������ ����� �� 100% �� ������� � ������� ����� �� �������� � ����� ���������� ���� ������
        /// </summary>
        public double? remain = null;

        /// <summary>
        /// ������� ����� ������� �������. �������� ��� �������� ��� ���� � �������� ���� ��������� �� ������ �������
        /// </summary>
        public double? timeout = null;

        public object data;
        public DateTime finish = DateTime.Now;
    }
}
