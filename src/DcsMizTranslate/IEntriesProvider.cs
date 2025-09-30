using System;

namespace DcsMizTranslate;

public interface IEntriesProvider
{
    string Name { get; }
    Task<Dictionary<string, string>> GetEntriesAsync(string filePath, CancellationToken cancellationToken);

    Task WriteEntriesAsync(string filePath, string languageCode, Dictionary<string, string> entries, CancellationToken cancellationToken);
}
