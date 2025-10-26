using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace DcsMizTranslate;

public partial class LuaTokenEntriesProvider : IEntriesProvider
{
    public string Name => "DCS Lua Token Entries Provider";

    private TokenName[] _supportedVariableTokens = [new("subtitle"), new("outText")];

    // new[] { "subtitle", "outText" };
    private partial record TokenName(string Name, bool IsMethod = false)
    {
        public IEnumerable<string> ReadTokens(string? rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return [];
            }

            if (IsMethod)
            {
                // todo: extract method parameters such as ['Parameter1', 1, VAR]
                return [];
            }

            return GetVariableValueExpressionTokens(rawText);
        }

        public bool TryReplaceTokens(string rawText,
            Dictionary<int, string> replaceTokens,
            [NotNullWhen(true)] out string? replacedText)
        {
            replacedText = null;
            if (string.IsNullOrWhiteSpace(rawText) || replaceTokens.Count == 0)
            {
                return false;
            }

            if (IsMethod)
            {
                return false;
            }

            return ReplaceVariableValueTokens(rawText, replaceTokens, out replacedText);
        }


        private bool ReplaceVariableValueTokens(string rawText, Dictionary<int, string> replaceTokens, out string? replacedText)
        {
            replacedText = null;
            var expr = GetVariableValueExpression(rawText);

            if (string.IsNullOrEmpty(expr))
            {
                return false;
            }

            var tokenMatch = GetConstantStringValMatcher();
            var newExpr = tokenMatch.Replace(expr, m =>
            {
                var wrapper = m.Value[0];
                if (replaceTokens.TryGetValue(m.Index, out var newValue))
                {
                    return $"{wrapper}{newValue}{wrapper}";
                }
                else
                {
                    return m.Value;
                }
            });

            replacedText = $"{Name} = {newExpr}";

            return true;
        }

        /// <summary>
        /// extract the variable value expression from a line of Lua code
        /// <para>
        /// e.g. subtitle = "Hello"..myvar.."!"  -> "Hello"..myvar.."!"
        /// </para>
        /// </summary>
        /// <param name="rawText"></param>
        /// <returns></returns>
        private string GetVariableValueExpression(string rawText)
        {
            // Name = expr
            var regex = new Regex($@"{Name}\s*=\s*(?<expr>.+)");

            var match = regex.Match(rawText);

            if (!match.Success)
            {
                return string.Empty;
            }

            return match.Groups["expr"].Value;
        }

        /// <summary>
        /// Extract string constants from a variable value expression
        /// <para>
        /// e.g. "Hello"..myvar.."!"  -> [ "Hello", "!" ]
        /// </para>
        /// </summary>
        /// <param name="rawText"></param>
        /// <returns></returns>
        private string[] GetVariableValueExpressionTokens(string rawText)
        {
            var expr = GetVariableValueExpression(rawText);

            if (string.IsNullOrEmpty(expr))
            {
                return [];
            }

            // example value: 
            // - "Hello"..myvar.."!"  -> [ "Hello", "!" ]
            // - "Hello World" -> [ "Hello World" ]
            // - var2 -> []
            // 'Hello'..myvar..'!'  -> [ 'Hello', '!' ]
            // 'Hello World' -> [ 'Hello World' ]
            var tokenMatch = GetConstantStringValMatcher();
            var constants = tokenMatch.Matches(expr)
                .Select(m => m.Groups["text"].Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            return constants;
        }

        [GeneratedRegex("[\"'](?<text>(?:[^\"\\\\]|\\\\.)*)[\"']", RegexOptions.Compiled)]
        private static partial Regex GetConstantStringValMatcher();
    }

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
                foreach (var tokenName in _supportedVariableTokens)
                {
                    var tokens = tokenName.ReadTokens(content);

                    var key = $"{entry.FullName}#{rowNum}";
                    foreach (var (token, i) in tokens.Select((x, i) => (x, i)))
                    {
                        // eg. a.lua#12@0
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

                var translatedTokens = entries.Where(x => x.Key.StartsWith($"{entry.FullName}#{rowNum}@"))
                    .ToDictionary(x => int.Parse(x.Key.Replace($"{entry.FullName}#{rowNum}@", string.Empty)), x => x.Value.Replace("\"", "\\\"").Replace("\n", "\\n"));

                if (translatedTokens.Count == 0 || string.IsNullOrEmpty(content))
                {
                    newContents.Add(content ?? string.Empty);
                    continue;
                }

                foreach (var tokenName in _supportedVariableTokens)
                {
                    if (tokenName.TryReplaceTokens(content, translatedTokens, out var newContent))
                    {
                        newContents.Add(newContent);
                        break;
                    }
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
}
