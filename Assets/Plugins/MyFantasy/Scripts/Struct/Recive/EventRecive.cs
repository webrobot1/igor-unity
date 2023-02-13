using System;
using System.Collections.Generic;

namespace MyFantasy
{
    public class EventRecive
    {
        public string action = "";

        public double? remain = null;
        public double? timeout = null;

        public object data;
        public DateTime finish = DateTime.Now;
    }
}
