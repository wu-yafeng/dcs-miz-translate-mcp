using System;
using System.Buffers;
using System.ComponentModel;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using NLua;
using Microsoft.Extensions.AI;
using Microsoft.VisualBasic.FileIO;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace DcsMizTranslate.Tools;

public record ContentResult(string TranslatedContent);
public class DcsMizTranslateTools
{
    private readonly static JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    [McpServerTool]
    [Description("Translate the specified .miz file.")]
    public async Task<AIContent> TranslateMizFileAsync(
        McpServer thisServer,
        IProgress<ProgressNotificationValue> progress,
        [Description("The path of .miz file.")] string filePath,
        [Description("The language code translate to. For exmaple: CN")] string languageCode, CancellationToken cancellationToken = default)
    {
        var entries = await GetResourceEntriesAsync(filePath, cancellationToken);

        var cacheFilePath = Path.Combine(SpecialDirectories.MyDocuments,
            "DCSMizTranslate",
            languageCode,
            "cache.json");

        var cache = await GetCachedEntriesAsync(cacheFilePath, cancellationToken);

        var task = TranslateAsync(thisServer, progress, entries, cache, languageCode, cancellationToken);

        try
        {
            await task;
        }
        catch (Exception ex)
        {
            return new ErrorContent("Translation failed: " + ex.Message);
        }
        finally
        {
            await File.WriteAllTextAsync(cacheFilePath, JsonSerializer.Serialize(cache, _jsonOptions), cancellationToken);
        }

        await WriteTranslatedEntriesAsync(filePath, languageCode, entries, cancellationToken);

        return new TextContent("Translate completed. The cache file written to: " + cacheFilePath);
    }

    private async Task<Dictionary<string, string>> GetCachedEntriesAsync(string filePath, CancellationToken cancellationToken)
    {
        var directoryPath = Path.GetDirectoryName(filePath);

        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath!);
        }

        if (!File.Exists(filePath))
        {
            return [];
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(filePath, cancellationToken), _jsonOptions) ?? [];
    }

    private async Task<Dictionary<string, string>> GetResourceEntriesAsync(string filePath, CancellationToken cancellationToken)
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

    private async Task TranslateAsync(McpServer thisServer,
        IProgress<ProgressNotificationValue> progress,
        Dictionary<string, string> entries,
        Dictionary<string, string> cache,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var prompt = """
             - 不要翻译专有名词 如 飞机的呼号 等
             - 不要包含任何其他文本内容
             - 格式与输入相同
            """;

        var options = new ChatOptions()
        {
            Instructions = prompt,
        };

        var current = 0;

        var contents = entries.Where(x => !x.Key.StartsWith("DictKey_ActionRadioText") && x.Value.Length > 15).Select(x => x.Value).Distinct().ToList();

        foreach (var content in contents)
        {
            progress.Report(new()
            {
                Progress = current++,
                Total = contents.Count,
                Message = $"Translating entry [{current}/{contents.Count}]"
            });

            if (cache.TryGetValue(content, out var cachedTranslation))
            {
                continue;
            }

            var response = await thisServer.AsSamplingChatClient()
                .GetResponseAsync<ContentResult>($"Translate content to {languageCode}: '{content}'", options, cancellationToken: cancellationToken);

            if (response.TryGetResult(out var r))
            {
                cache[content] = r.TranslatedContent;
            }
            else
            {
                cache[content] = response.Text;
            }
        }

        foreach (var entry in entries)
        {
            if (cache.TryGetValue(entry.Value, out var translated))
            {
                entries[entry.Key] = translated;
            }
        }
    }


    private async Task WriteTranslatedEntriesAsync(string filePath,
            string languageCode,
            Dictionary<string, string> translatedEntries,
            CancellationToken cancellationToken = default)
    {
        var luaTable = "dictionary = {\n";
        foreach (var kvp in translatedEntries)
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
