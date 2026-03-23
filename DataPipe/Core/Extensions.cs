using System;
using System.Text.Json;

namespace DataPipe.Core
{
    /// <summary>
    /// A helper class to easily view message contents as JSON for debugging purposes
    /// </summary>
    public static class Extensions
    {
        public static string Dump(this Object o)
        {
            return JsonSerializer.Serialize(o);
        }
    }
}