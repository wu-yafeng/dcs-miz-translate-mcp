using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DcsMizTranslate;

public class LuaTokenEntriesProvider : IEntriesProvider
{
    public string Name => "DCS Lua Token Entries Provider";

    public async Task<Dictionary<string, string>> GetEntriesAsync(string filePath, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>();
        using var mizArchive = ZipFile.OpenRead(filePath);

        // find all localization contents in l10n/DEFAULT/*.lua
        foreach (var entry in mizArchive.Entries.Where(x => x.FullName.StartsWith("l10n/DEFAULT/") && x.FullName.EndsWith(".lua")))
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);

            var rowNum = 0;
            do
            {
                rowNum++;
                var content = await reader.ReadLineAsync(cancellationToken);

                if (!string.IsNullOrEmpty(content))
                {
                    // subtitle = "Hello"..myvar.."!" -> "Hello"..myvar.."!"
                    var expr = GetExpr(content);
                    if (string.IsNullOrEmpty(expr))
                    {
                        continue;
                    }
                    // "Hello"..myvar.."!" -> [ "Hello", "!" ]
                    var tokens = ExtractTokens(expr);

                    var key = $"{entry.FullName}#{rowNum}";
                    foreach (var (token, i) in tokens.Select((x, i) => (x, i)))
                    {
                        var tokenKey = $"{key}@{i}";
                        if (!result.ContainsKey(tokenKey))
                        {
                            result[tokenKey] = token;
                        }
                    }
                }

            } while (!reader.EndOfStream);

        }
        return result;
    }

    public async Task WriteEntriesAsync(string filePath, string languageCode, Dictionary<string, string> entries, CancellationToken cancellationToken)
    {
        using var mizArchive = ZipFile.Open(filePath, ZipArchiveMode.Update);

        // find all localization contents in l10n/DEFAULT/*.lua
        var entriesToProcess = mizArchive.Entries.Where(x => x.FullName.StartsWith("l10n/DEFAULT/") && x.FullName.EndsWith(".lua")).ToArray();
        foreach (var entry in entriesToProcess)
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);

            var entryPath = $"l10n/{languageCode}/{entry.Name}";
            var existingEntry = mizArchive.GetEntry(entryPath);
            existingEntry?.Delete();

            var newEntry = mizArchive.CreateEntry(entryPath);
            using var entryStream = newEntry.Open();
            using var writer = new StreamWriter(entryStream);

            var rowNum = 0;
            var newContents = new List<string>();
            do
            {
                rowNum++;
                var content = await reader.ReadLineAsync(cancellationToken);

                if (content is null)
                {
                    continue;
                }

                if (content != string.Empty)
                {
                    var expr = GetExpr(content);
                    // we dont need to process lines without subtitle = ...
                    if (string.IsNullOrEmpty(expr))
                    {
                        newContents.Add(content);
                        continue;
                    }
                    // extract tokens from the expression
                    // e.g. "Hello"..myvar.."!" -> [ "Hello", "!"
                    var tokens = ExtractTokens(expr).ToArray();

                    if (tokens.Length == 0)
                    {
                        newContents.Add(content);
                        continue;
                    }

                    var key = $"{entry.FullName}#{rowNum}";
                    for (var i = 0; i < tokens.Length; i++)
                    {
                        var tokenKey = $"{key}@{i}";
                        if (entries.TryGetValue(tokenKey, out var newValue))
                        {
                            tokens[i] = newValue;
                        }
                        expr = ReplaceToken(expr, i, tokens[i]);
                    }

                    var newContent = $"subtitle = {expr}";

                    newContents.Add(newContent);
                }
                else
                {
                    newContents.Add(content);
                }

            } while (!reader.EndOfStream);

            // rewrite the entry content
            foreach (var newContent in newContents)
            {
                await writer.WriteLineAsync(newContent.AsMemory(), cancellationToken);
            }
            await writer.FlushAsync();
        }
    }

    private string GetExpr(string content)
    {
        var regex = new Regex(@"subtitle\s*=\s*(?<expr>.+)");

        var match = regex.Match(content);

        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Groups["expr"].Value;
    }

    private IEnumerable<string> ExtractTokens(string expr)
    {
        var tokenMatch = new Regex("\"(?<text>(?:[^\"\\\\]|\\\\.)*)\"");
        var constants = tokenMatch.Matches(expr).Select(m => m.Groups["text"].Value).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        return constants;
    }

    private string ReplaceToken(string expr, int index, string newValue)
    {
        var tokenMatch = new Regex("\"(?<text>(?:[^\"\\\\]|\\\\.)*)\"");
        var currentIndex = 0;
        var result = tokenMatch.Replace(expr, m =>
        {
            if (currentIndex == index)
            {
                currentIndex++;
                return $"\"{newValue}\"";
            }
            else
            {
                currentIndex++;
                return m.Value;
            }
        });

        return result;
    }
}
