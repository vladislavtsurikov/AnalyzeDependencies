using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.Core.Utilities
{
    public static class NamespaceUtility
    {
        public static List<string> GetNamespacesFromAssemblyName(string assemblyName)
        {
            List<string> namespaces = new List<string> { assemblyName };

            if (assemblyName.Contains("."))
            {
                string[] parts = assemblyName.Split('.');
                for (int i = 1; i <= parts.Length; i++)
                {
                    namespaces.Add(string.Join(".", parts.Take(i)));
                }
            }

            return namespaces;
        }

        public static HashSet<string> GetNamespacesDeclaredInFiles(List<string> csFiles)
        {
            var namespaces = new HashSet<string>();
            var namespacePattern = new Regex(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)");

            foreach (string csFile in csFiles)
            {
                try
                {
                    string content = File.ReadAllText(csFile);

                    foreach (Match match in namespacePattern.Matches(content))
                    {
                        if (match.Groups.Count > 1)
                        {
                            string ns = match.Groups[1].Value;
                            if (!string.IsNullOrWhiteSpace(ns))
                            {
                                namespaces.Add(ns);
                            }
                        }
                    }
                }
                catch (System.Exception)
                {
                }
            }

            return namespaces;
        }

        public static bool IsNamespaceUsedInFiles(List<string> csFiles, List<string> namespaces)
        {
            foreach (string csFile in csFiles)
            {
                try
                {
                    string content = File.ReadAllText(csFile);

                    foreach (string ns in namespaces)
                    {
                        if (Regex.IsMatch(content, $@"\busing\s+(static\s+)?{Regex.Escape(ns)}\s*;"))
                            return true;

                        if (Regex.IsMatch(content, $@"\busing\s+\w+\s*=\s*{Regex.Escape(ns)}\s*;"))
                            return true;

                        if (Regex.IsMatch(content, $@"\busing\s+(static\s+)?{Regex.Escape(ns)}\."))
                            return true;

                        if (Regex.IsMatch(content, $@"\b{Regex.Escape(ns)}\s*\."))
                            return true;
                    }
                }
                catch (System.Exception)
                {
                }
            }

            return false;
        }
    }
}
