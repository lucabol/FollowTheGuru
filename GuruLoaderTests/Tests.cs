using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using System.Linq;


public class Tests {


    public static IEnumerable<object[]> GetFileWithExtension(string ext)
        => Directory.GetFiles(@"TestData\", ext)
                    .Select(path => new object [] { path});

    public static IEnumerable<object[]>  GetRssFiles() => GetFileWithExtension("*.rss");
    public static IEnumerable<object[]> GetHtmFiles() => GetFileWithExtension("*.htm");

    [Theory, MemberData(nameof(GetHtmFiles))]
    public void ParseHtmFileTests(string fileFullName) {
        var htmStream = File.OpenRead(fileFullName);
        var link = GuruLoader.ParseHtmFile(htmStream);
        Assert.False(String.IsNullOrEmpty(link));
        Assert.Equal(Path.GetExtension(link),  ".txt");
    }

    [Theory, MemberData(nameof(GetRssFiles))]
    public void ParseRssTextTest(string fileFullName) {
        var rssStream = File.OpenRead(fileFullName);
        var res = GuruLoader.ParseRssText(rssStream);
        Assert.NotNull(res);
        Assert.False(String.IsNullOrEmpty(res.DisplayName));
        Assert.NotEmpty(res.Links);

        Assert.All(res.Links, link => Assert.True(Path.GetExtension(link) == ".htm" ||
                                                  Path.GetExtension(link) == ".html")) ;
    }
}

