using DcsMizTranslate;

namespace UnitTests;

public class LuaRegexMatchedEntriesProviderTests
{
    [Fact]
    public async Task RunTest()
    {
        const string fileName = @"..\..\artifacts\RAM M02.miz";
        var matcher = new LuaTokenEntriesProvider();

        var entries = await matcher.GetEntriesAsync(fileName, TestContext.Current.CancellationToken);

        foreach (var (key, value) in entries)
        {
            entries[key] = value + "_translated";
        }

        await matcher.WriteEntriesAsync(fileName, "CN", entries, TestContext.Current.CancellationToken);
    }
}
