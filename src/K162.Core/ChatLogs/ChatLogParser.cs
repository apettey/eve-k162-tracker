using System.Globalization;

namespace K162.Core.ChatLogs;

/// <summary>
/// Pure parsing helpers for EVE Local chat log files
/// (Documents\EVE\logs\Chatlogs\Local_YYYYMMDD_HHMMSS[_CHARID].txt, UTF-16LE).
/// </summary>
public static class ChatLogParser
{
    private const string SystemChangeMarker = "Channel changed to Local : ";

    /// <summary>Extracts the character id from a modern log filename, e.g. Local_20260905_102345_2112625428.txt.</summary>
    public static long? CharacterIdFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split('_');
        return parts.Length >= 4 && long.TryParse(parts[3], out var id) ? id : null;
    }

    /// <summary>Session timestamp from the filename (used to pick the newest log per character).</summary>
    public static DateTime? SessionStampFromFileName(string fileName)
    {
        var parts = Path.GetFileNameWithoutExtension(fileName).Split('_');
        if (parts.Length >= 3 &&
            DateTime.TryParseExact(parts[1] + parts[2], "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return null;
    }

    /// <summary>Reads the "Listener:" character name from the log header block.</summary>
    public static string? ListenerFromHeader(string headerText)
    {
        foreach (var raw in headerText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("Listener:", StringComparison.OrdinalIgnoreCase))
                return line["Listener:".Length..].Trim();
        }
        return null;
    }

    /// <summary>
    /// Parses "[ 2026.09.05 10:31:02 ] EVE System > Channel changed to Local : J115844".
    /// Returns false for every other line.
    /// </summary>
    public static bool TryParseSystemChange(string line, out string systemName, out DateTimeOffset time)
    {
        systemName = "";
        time = default;
        var idx = line.IndexOf(SystemChangeMarker, StringComparison.Ordinal);
        if (idx < 0) return false;
        systemName = line[(idx + SystemChangeMarker.Length)..].Trim().TrimEnd('*');
        if (systemName.Length == 0) return false;

        var open = line.IndexOf('[');
        var close = line.IndexOf(']');
        if (open >= 0 && close > open &&
            DateTimeOffset.TryParseExact(line[(open + 1)..close].Trim(), "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            time = parsed;
        else
            time = DateTimeOffset.UtcNow;
        return true;
    }

    /// <summary>The last system-change line in a chunk of log text, or null.</summary>
    public static (string SystemName, DateTimeOffset Time)? LastSystemChange(IEnumerable<string> lines)
    {
        (string, DateTimeOffset)? last = null;
        foreach (var line in lines)
            if (TryParseSystemChange(line, out var sys, out var t))
                last = (sys, t);
        return last;
    }
}
