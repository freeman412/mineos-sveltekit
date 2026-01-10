namespace MineOS.Infrastructure.Utilities;

public static class IniParser
{
    /// <summary>
    /// Parse a simple INI file (key=value format, no sections)
    /// Used for server.properties
    /// </summary>
    public static Dictionary<string, string> ParseSimple(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmed.Substring(0, eqIndex).Trim();
            var value = trimmed.Substring(eqIndex + 1).Trim();

            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// Write a simple INI file (key=value format)
    /// </summary>
    public static string WriteSimple(Dictionary<string, string> data)
    {
        var lines = data.Select(kvp => $"{kvp.Key}={kvp.Value}");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// Parse an INI file with sections ([section])
    /// Used for server.config
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> ParseWithSections(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = "default";
        result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                continue;

            // Check for section header
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (!result.ContainsKey(currentSection))
                {
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }

            // Parse key=value
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmed.Substring(0, eqIndex).Trim();
            var value = trimmed.Substring(eqIndex + 1).Trim();

            result[currentSection][key] = value;
        }

        return result;
    }

    /// <summary>
    /// Write an INI file with sections
    /// </summary>
    public static string WriteWithSections(Dictionary<string, Dictionary<string, string>> data)
    {
        var lines = new List<string>();

        foreach (var section in data)
        {
            // Skip empty sections
            if (section.Value.Count == 0)
                continue;

            // Don't write [default] section header
            if (section.Key != "default")
            {
                lines.Add($"[{section.Key}]");
            }

            foreach (var kvp in section.Value)
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }

            lines.Add(""); // Empty line between sections
        }

        return string.Join("\n", lines);
    }
}
