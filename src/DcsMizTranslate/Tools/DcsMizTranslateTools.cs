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
        IEnumerable<IEntriesProvider> entriesProviders,
        [Description("The path of .miz file.")] string filePath,
        [Description("The language code translate to. For exmaple: CN")] string languageCode, CancellationToken cancellationToken = default)
    {
        var cacheFilePath = Path.Combine(SpecialDirectories.MyDocuments,
            "DCSMizTranslate",
            languageCode,
            "cache.json");

        var cache = await GetCachedEntriesAsync(cacheFilePath, cancellationToken);

        foreach (var provider in entriesProviders)
        {
            var entries = await provider.GetEntriesAsync(filePath, cancellationToken);

            var task = TranslateAsync(provider.Name, thisServer, progress, entries, cache, languageCode, cancellationToken);
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

            await provider.WriteEntriesAsync(filePath, languageCode, entries, cancellationToken);
        }

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
    private async Task TranslateAsync(
        string providerName,
        McpServer thisServer,
        IProgress<ProgressNotificationValue> progress,
        Dictionary<string, string> entries,
        Dictionary<string, string> cache,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var prompt = """
             你是一个美国军事翻译专家，精通军事任务相关知识，下面是跟美国战斗机任务（DCS模拟飞行游戏）相关的英语，翻译成简体中文，要求：
             - 优先识别文本中的人名和呼号或者代号，保持原文不翻译，再翻译其他部分
             - 纯文本输出
             - 保持原文的换行格式
             - 仅作为翻译不要续写
             - 原文和翻译词数不能相差过大
             - 仅输出翻译结果，不注释不解释
             - 俚语直译（如“Break left!”→“向左急转！”；“Light”→“导弹”；“hot”→“迎头”；“cold”→“尾追”；“angels”→“高度”）
             - 坐标格式（如“three-four-zero at sixty”→“340度60海里”）；呼号格式（如“Anvil one-two”→“Anvil 1-2”）
             - 遵守单位强制（如“20 miles”→“20海里”）
             - 特殊代号（如“rock”保持原文）
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
                Message = $"Translating {providerName} [{current}/{contents.Count}]"
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
}
