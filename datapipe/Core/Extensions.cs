using System;
using System.Text.Json;

namespace DataPipe.Core
{
    public static class Extensions
    {
        public static string Dump(this Object o)
        {
            return JsonSerializer.Serialize(o);
        }
    }
}