using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

// Self-test for the shared ParseForm helper so request-body assertions in the
// rest of the suite are trusted to decode application/x-www-form-urlencoded
// payloads exactly: '+' is a space, %2B is a literal '+'.
public sealed class ParseFormTests
{
    [Fact]
    public void ParseFormDecodesPlusAsSpaceAndPercentEncodedPlusAsLiteralPlus()
    {
        var form = ParseForm("spaced=a+b&literal_plus=a%2Bb&mixed=a%2B+b&key%2Bname=1&empty=");

        Assert.Equal(5, form.Count);
        Assert.Equal("a b", form["spaced"]);
        Assert.Equal("a+b", form["literal_plus"]);
        Assert.Equal("a+ b", form["mixed"]);
        Assert.Equal("1", form["key+name"]);
        Assert.Equal(string.Empty, form["empty"]);
    }

    [Fact]
    public async Task ParseFormRoundTripsFormUrlEncodedContent()
    {
        var fields = new Dictionary<string, string>
        {
            ["search"] = "search index=\"team\" earliest=-1h@h | stats count AS error_count",
            ["literal_plus"] = "duration+jitter",
            ["spaced key"] = "a b c",
            ["empty"] = string.Empty
        };

        using var content = new FormUrlEncodedContent(fields);
        var form = ParseForm(await content.ReadAsStringAsync());

        Assert.Equal(fields, form);
    }
}
