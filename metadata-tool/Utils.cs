using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetadataTool
{
    internal static class Utils
    {
        public static T GetArg<T>(string[] args, string argName, T defaultValue = default)
        {
            int index = Array.IndexOf(args, argName);
            if (index >= 0 && args.Length > index + 1)
            {
                string rawArg = args[index + 1];
                if (typeof(T).IsAssignableFrom(typeof(string)))
                    return (T)(object)rawArg;

                return (T)Convert.ChangeType(rawArg, typeof(T));
            }

            return defaultValue;
        }

    }
}
