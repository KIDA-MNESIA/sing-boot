using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Web.Script.Serialization;

namespace SingBoot.Tests;

[TestClass]
public sealed class JsonHelperTests
{
    [TestMethod]
    public void NormalizeJson_RemovesCommentsAndTrailingCommasWithoutChangingStrings()
    {
        const string source = """
                              {
                                // comment
                                "url": "https://example.test/a//b",
                                "values": [1, 2,],
                              }
                              """;

        var normalized = JsonHelper.NormalizeJson(source);
        var root = (IDictionary<string, object>)new JavaScriptSerializer().DeserializeObject(normalized);
        var values = (object[])root["values"];

        Assert.AreEqual("https://example.test/a//b", root["url"]);
        CollectionAssert.AreEqual(new object[] { 1, 2 }, values);
        Assert.AreEqual(CountLines(source), CountLines(normalized));
    }

    [TestMethod]
    [DataRow("[1,,2]")]
    [DataRow("{\"port\": 1 2}")]
    [DataRow("{\"value\": 1 /* gap */ 2}")]
    public void NormalizeJson_DoesNotRepairInvalidTokenSequences(string source)
    {
        var normalized = JsonHelper.NormalizeJson(source);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new JavaScriptSerializer().DeserializeObject(normalized));
    }

    [TestMethod]
    public void NormalizeJson_RejectsUnterminatedBlockComment()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            JsonHelper.NormalizeJson("{\"value\": 1 /* missing end"));
    }

    private static int CountLines(string value)
    {
        return value.Count(ch => ch == '\n') + 1;
    }
}
