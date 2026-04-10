using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VladislavTsurikov.AnalyzeDependencies.Editor.Core.Utilities
{
    public static class PackageJsonUtility
    {
        private const string InternalPackagePrefix = "com.vladislavtsurikov.";
        private static readonly string[] AssemblyPrefixes =
        {
            "VladislavTsurikov.",
            "VladislavTrurikov."
        };

        private static readonly Regex WhitespacesRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string GetPackageRoot(string asmdefPath)
        {
            return Path.GetDirectoryName(asmdefPath);
        }

        public static string GetPackageJsonPath(string packageRoot)
        {
            return Path.Combine(packageRoot, "package.json");
        }

        public static bool HasPackageJson(string packageRoot)
        {
            return File.Exists(GetPackageJsonPath(packageRoot));
        }

        public static bool TryFindNearestPackageJson(string asmdefPath, out string packageJsonPath)
        {
            packageJsonPath = null;

            string assemblyDirectory = Path.GetDirectoryName(asmdefPath);
            if (!string.IsNullOrEmpty(assemblyDirectory) && Directory.Exists(assemblyDirectory))
            {
                string[] localPackageFiles = Directory.GetFiles(assemblyDirectory, "package.json", SearchOption.AllDirectories);
                if (localPackageFiles.Length > 0)
                {
                    packageJsonPath = localPackageFiles
                        .OrderBy(path => path.Length)
                        .First();
                    return true;
                }
            }

            string currentDirectory = assemblyDirectory;
            while (!string.IsNullOrEmpty(currentDirectory))
            {
                string candidate = Path.Combine(currentDirectory, "package.json");
                if (File.Exists(candidate))
                {
                    packageJsonPath = candidate;
                    return true;
                }

                DirectoryInfo parent = Directory.GetParent(currentDirectory);
                currentDirectory = parent?.FullName;
            }

            return false;
        }

        public static string BuildPackageName(string assemblyName)
        {
            string normalized = GetPackageShortName(assemblyName);
            normalized = normalized.Trim('.');
            if (string.IsNullOrEmpty(normalized))
                return InternalPackagePrefix.TrimEnd('.');

            string packageSuffix = normalized
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.ToLowerInvariant())
                .Aggregate((left, right) => $"{left}.{right}");

            return InternalPackagePrefix + packageSuffix;
        }

        public static string BuildDisplayName(string assemblyName)
        {
            string normalized = GetPackageShortName(assemblyName);
            if (string.IsNullOrWhiteSpace(normalized))
                return "Package";

            return normalized.Replace('.', ' ');
        }

        public static string BuildDescription(string assemblyName)
        {
            return $"UPM package for {BuildDisplayName(assemblyName)}.";
        }

        public static string CreateMinimalPackageJson(string assemblyName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"name\": \"{BuildPackageName(assemblyName)}\",");
            sb.AppendLine("  \"version\": \"1.0.0\",");
            sb.AppendLine($"  \"displayName\": \"{BuildDisplayName(assemblyName)}\",");
            sb.AppendLine("  \"unity\": \"2021.3\",");
            sb.AppendLine($"  \"description\": \"{BuildDescription(assemblyName)}\"");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static bool TryReadPackageMetadata(string packageJsonPath, out string packageName, out string version)
        {
            packageName = null;
            version = null;

            if (!File.Exists(packageJsonPath))
                return false;

            try
            {
                string json = File.ReadAllText(packageJsonPath);
                return TryReadPackageMetadataFromJson(json, out packageName, out version);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryReadPackageMetadataFromJson(string json, out string packageName, out string version)
        {
            packageName = null;
            version = null;

            if (!TryParseTopLevelProperties(json, out List<JsonPropertyEntry> properties))
                return false;

            JsonPropertyEntry nameEntry = properties.FirstOrDefault(property => property.Name == "name");
            JsonPropertyEntry versionEntry = properties.FirstOrDefault(property => property.Name == "version");

            if (nameEntry != null)
                packageName = TrimJsonString(nameEntry.RawValue);

            if (versionEntry != null)
                version = TrimJsonString(versionEntry.RawValue);

            return !string.IsNullOrEmpty(packageName);
        }

        public static bool TryReadPackageDependencies(string packageJsonPath, out Dictionary<string, string> dependencies)
        {
            dependencies = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!File.Exists(packageJsonPath))
                return false;

            try
            {
                string json = File.ReadAllText(packageJsonPath);
                return TryReadPackageDependenciesFromJson(json, out dependencies);
            }
            catch
            {
                dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
                return false;
            }
        }

        public static bool TryReadPackageDependenciesFromJson(string json, out Dictionary<string, string> dependencies)
        {
            dependencies = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!TryParseTopLevelProperties(json, out List<JsonPropertyEntry> properties))
                return false;

            JsonPropertyEntry dependenciesEntry = properties.FirstOrDefault(property => property.Name == "dependencies");
            if (dependenciesEntry == null)
                return true;

            return TryParseStringDictionary(dependenciesEntry.RawValue, out dependencies);
        }

        public static string UpsertDependencies(string json, IReadOnlyDictionary<string, string> dependencies)
        {
            if (!TryParseTopLevelProperties(json, out List<JsonPropertyEntry> properties))
                throw new InvalidDataException("package.json is not a valid top-level JSON object.");

            int existingIndex = properties.FindIndex(property => property.Name == "dependencies");
            if (existingIndex >= 0)
            {
                properties.RemoveAt(existingIndex);
            }

            if (dependencies != null && dependencies.Count > 0)
            {
                var sortedDependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> dependency in dependencies)
                {
                    sortedDependencies[dependency.Key] = dependency.Value;
                }

                var entry = new JsonPropertyEntry
                {
                    Name = "dependencies",
                    RawValue = SerializeDependenciesObject(sortedDependencies)
                };

                if (existingIndex >= 0)
                {
                    properties.Insert(existingIndex, entry);
                }
                else
                {
                    properties.Add(entry);
                }
            }

            return SerializeTopLevelProperties(properties);
        }

        public static string NormalizeForComparison(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string lower = value.ToLowerInvariant()
                .Replace("com.", string.Empty)
                .Replace("unityengine.", string.Empty)
                .Replace("unity.", string.Empty)
                .Replace("vladislavtsurikov.", string.Empty);

            return WhitespacesRegex.Replace(lower.Replace('-', ' ').Replace('_', ' ').Replace('.', ' '), " ").Trim();
        }

        private static string GetPackageShortName(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                return string.Empty;

            foreach (string prefix in AssemblyPrefixes)
            {
                if (assemblyName.StartsWith(prefix, StringComparison.Ordinal))
                    return assemblyName.Substring(prefix.Length);
            }

            return assemblyName;
        }

        private static string SerializeDependenciesObject(IReadOnlyDictionary<string, string> dependencies)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            int index = 0;
            foreach (KeyValuePair<string, string> dependency in dependencies)
            {
                bool isLast = index == dependencies.Count - 1;
                sb.AppendLine($"    \"{dependency.Key}\": \"{dependency.Value}\"{(isLast ? string.Empty : ",")}");
                index++;
            }

            sb.Append("  }");
            return sb.ToString();
        }

        private static string SerializeTopLevelProperties(IReadOnlyList<JsonPropertyEntry> properties)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            for (int i = 0; i < properties.Count; i++)
            {
                JsonPropertyEntry property = properties[i];
                bool isLast = i == properties.Count - 1;
                sb.Append("  \"");
                sb.Append(property.Name);
                sb.Append("\": ");
                sb.Append(property.RawValue);

                if (!isLast)
                {
                    sb.Append(',');
                }

                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static bool TryParseStringDictionary(string json, out Dictionary<string, string> dictionary)
        {
            dictionary = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!TryParseTopLevelProperties(json, out List<JsonPropertyEntry> properties))
                return false;

            foreach (JsonPropertyEntry property in properties)
            {
                dictionary[property.Name] = TrimJsonString(property.RawValue);
            }

            return true;
        }

        private static bool TryParseTopLevelProperties(string json, out List<JsonPropertyEntry> properties)
        {
            properties = new List<JsonPropertyEntry>();

            if (string.IsNullOrWhiteSpace(json))
                return false;

            int index = SkipWhitespace(json, 0);
            if (index >= json.Length || json[index] != '{')
                return false;

            index++;

            while (index < json.Length)
            {
                index = SkipWhitespace(json, index);
                if (index >= json.Length)
                    return false;

                if (json[index] == '}')
                    return true;

                if (json[index] != '"')
                    return false;

                int nameStart = index;
                int nameEnd = FindStringEnd(json, nameStart);
                string propertyName = UnescapeJsonString(json.Substring(nameStart + 1, nameEnd - nameStart - 1));

                index = SkipWhitespace(json, nameEnd + 1);
                if (index >= json.Length || json[index] != ':')
                    return false;

                index = SkipWhitespace(json, index + 1);
                int valueStart = index;
                int valueEnd = FindValueEnd(json, valueStart);
                if (valueEnd < valueStart)
                    return false;

                properties.Add(new JsonPropertyEntry
                {
                    Name = propertyName,
                    RawValue = json.Substring(valueStart, valueEnd - valueStart + 1)
                });

                index = SkipWhitespace(json, valueEnd + 1);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < json.Length && json[index] == '}')
                    return true;
            }

            return false;
        }

        private static int SkipWhitespace(string json, int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
            {
                index++;
            }

            return index;
        }

        private static int FindStringEnd(string json, int startIndex)
        {
            bool escaped = false;

            for (int i = startIndex + 1; i < json.Length; i++)
            {
                char current = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindValueEnd(string json, int startIndex)
        {
            if (startIndex >= json.Length)
                return -1;

            char firstChar = json[startIndex];

            if (firstChar == '"')
            {
                return FindStringEnd(json, startIndex);
            }

            if (firstChar == '{' || firstChar == '[')
            {
                char opening = firstChar;
                char closing = firstChar == '{' ? '}' : ']';
                int depth = 0;
                bool insideString = false;
                bool escaped = false;

                for (int i = startIndex; i < json.Length; i++)
                {
                    char current = json[i];

                    if (insideString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                            continue;
                        }

                        if (current == '\\')
                        {
                            escaped = true;
                            continue;
                        }

                        if (current == '"')
                        {
                            insideString = false;
                        }

                        continue;
                    }

                    if (current == '"')
                    {
                        insideString = true;
                        continue;
                    }

                    if (current == opening)
                    {
                        depth++;
                    }
                    else if (current == closing)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return i;
                        }
                    }
                }

                return -1;
            }

            for (int i = startIndex; i < json.Length; i++)
            {
                char current = json[i];
                if (current == ',' || current == '}')
                {
                    return i - 1;
                }
            }

            return json.Length - 1;
        }

        private static string TrimJsonString(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return string.Empty;

            string trimmed = rawValue.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return UnescapeJsonString(trimmed.Substring(1, trimmed.Length - 2));
            }

            return trimmed;
        }

        private static string UnescapeJsonString(string value)
        {
            return value
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private sealed class JsonPropertyEntry
        {
            public string Name;
            public string RawValue;
        }
    }
}
