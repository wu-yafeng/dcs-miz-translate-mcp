using DcsMizTranslate;

namespace UnitTests;

public class LuaRegexMatchedEntriesProviderTests
{
    [Fact]
    public async Task RunTest()
    {
        const string fileName = @"..\..\..\..\..\artifacts\RAM M05.miz";
        var matcher = new LuaTokenEntriesProvider();
// C:\Users\yafen\source\repos\wu-yafeng\dcs-miz\tests\UnitTests\bin\artifacts\RAM M02.miz
        var entries = await matcher.GetEntriesAsync(fileName, TestContext.Current.CancellationToken);

        foreach (var (key, value) in entries)
        {
            entries[key] = value + "_translated";
        }

        await matcher.WriteEntriesAsync(fileName, "CN", entries, TestContext.Current.CancellationToken);
    }
}
