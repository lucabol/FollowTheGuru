using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using System.Linq;


public class Tests {

    public static IEnumerable<object[]> GetRssFiles() {
        return Directory
                .GetFiles(@"TestData\", "*.rss")
                .Select(path => new object [] { path});
    }

    [Theory, MemberData(nameof(GetRssFiles))]
    public void ParseRssTextTest(string fileFullName) {
        var rssStream = File.OpenRead(fileFullName);
        var res = GuruLoader.ParseRssText(rssStream);
        Assert.NotEqual(null, res);
        Assert.False(String.IsNullOrEmpty(res.DisplayName));
        Assert.NotEmpty(res.Links);
    }
}

