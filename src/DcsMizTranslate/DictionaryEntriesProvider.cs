using System;
using System.IO.Compression;
using NLua;

namespace DcsMizTranslate;

/// <summary>
/// Provides dictionary entries from a .miz file.
/// </summary>
public class DictionaryEntriesProvider : IEntriesProvider
{
    public string Name => "DCS Dictionary Entries Provider";

    public async Task<Dictionary<string, string>> GetEntriesAsync(string filePath, CancellationToken cancellationToken)
    {
        using var mizArchive = ZipFile.OpenRead(filePath);

        var defaultLocalizationEntry = mizArchive.GetEntry("l10n/DEFAULT/dictionary");

        if (defaultLocalizationEntry == null)
        {
            return [];
        }

        using var stream = defaultLocalizationEntry.Open();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);
        using var lua = new Lua();

        lua.DoString(content);

        var result = new Dictionary<string, string>();

        if (lua["dictionary"] is LuaTable table)
        {
            foreach (string key in table.Keys)
            {
                if (table[key] is string s)
                {
                    result.TryAdd(key, s);
                }
            }
        }

        return result;
    }

    public async Task WriteEntriesAsync(string filePath, string languageCode, Dictionary<string, string> entries, CancellationToken cancellationToken)
    {
        var luaTable = "dictionary = {\n";
        foreach (var kvp in entries)
        {
            var key = kvp.Key.Replace("\"", "\\\"");
            var value = kvp.Value.Replace("\"", "\\\"").Replace("\n", "\\n");
            luaTable += $"    [\"{key}\"] = \"{value}\",\n";
        }
        luaTable += "}";

        using var mizArchive = ZipFile.Open(filePath, ZipArchiveMode.Update);

        var entryPath = $"l10n/{languageCode}/dictionary";
        var existingEntry = mizArchive.GetEntry(entryPath);
        existingEntry?.Delete();

        var newEntry = mizArchive.CreateEntry(entryPath);

        using var entryStream = newEntry.Open();
        using var writer = new StreamWriter(entryStream);
        await writer.WriteAsync(luaTable);
    }
}
