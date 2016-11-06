using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

public class Position {

    private Position(DateTime date, string name, string classTitle, string cusip, int value, int shares, string sharesType) {
        Date = date;
        Name = name;
        ClassTitle = classTitle;
        Cusip = cusip;
        Value = value;
        Shares = shares;
        SharesType = sharesType;
    }
    public static Position Create(DateTime date, string name, string classTitle, string cusip, int value, int shares, string sharesType) {
        return new Position(date, name, classTitle, cusip, value, shares, sharesType);
    }

    public DateTime Date { get; }
    public string Name { get; }
    public string ClassTitle { get; }
    public string Cusip { get; }
    public int Value { get; }
    public int Shares { get; }
    public string SharesType { get; }

}

public class GuruData {
    public GuruData(string cik, string name, IEnumerable<Position> pos) {
        Name = name;
        Positions = pos;
        Cik = cik;
    }
    public string Cik { get; }
    public string Name { get; }
    public IEnumerable<Position> Positions { get; }
}

public class RssData {
    public string DisplayName { get; set; }
    public IEnumerable<string> Links { get; set; }
}

public static class GuruLoader {

    public static RssData ParseRssText(Stream rssStream) {
        var xml = XDocument.Load(rssStream);
        XNamespace xs = "http://www.w3.org/2005/Atom";
        var name = xml
                   .Descendants(xs + "company-info")
                   .First()
                   .Element(xs + "conformed-name").Value;

        var links = from feed in xml.Descendants(xs + "entry")
                    where feed.Element(xs + "content").Element(xs + "filing-type").Value == "13F-HR"
                    select feed.Element(xs + "link").Attribute("href").Value;

        return new RssData { DisplayName = name, Links = links };
    }

    static async Task<Tuple<string, IEnumerable<Tuple<string, string>>>> Load13FLinksAsync(string cik) {

        var cikLink = $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={cik}&CIK=0001568820&type=&dateb=&owner=exclude&start=0&count=40&output=atom";

        var txt = await Utils.HttpLoadAsync(cikLink);

        var xml = XDocument.Parse(txt);
        XNamespace xs = "http://www.w3.org/2005/Atom";

        var name = xml
                   .Descendants(xs + "company-info")
                   .First()
                   .Element(xs + "conformed-name").Value;

        var data = from feed in xml.Descendants(xs + "entry")
                   where feed.Element(xs + "content").Element(xs + "filing-type").Value == "13F-HR"
                   select new {
                       date = feed.Element(xs + "content").Element(xs + "filing-date").Value,
                       link = feed.Element(xs + "link").Attribute("href").Value
                   };

        var t1 = await Utils.HttpLoadAsync(data.First().link);
        var test = XElement.Parse(t1);

        var dateLinks = data
                        .Select(d => Tuple.Create(d.date, new Uri(d.link).Segments))
                        .Select(t => Tuple.Create(t.Item1, t.Item2.Take(t.Item2.Count() - 1))) // traverse each array twice, but fine
                        .Select(t => Tuple.Create(t.Item1, String.Join("", t.Item2)))
                        .Select(t => Tuple.Create(t.Item1, $"http://www.sec.gov{t.Item2}infotable.xml"));



        return Tuple.Create(name, dateLinks);
    }

    static IEnumerable<Position> PositionsFromText(string date, string txt) {

        XNamespace xs = "http://www.sec.gov/edgar/document/thirteenf/informationtable";
        var xml = XDocument.Parse(txt);
        var d = DateTime.Parse(date);

        return from it in xml.Descendants(xs + "infoTable")
               select Position.Create(d,
                                      it.Element(xs + "nameOfIssuer").Value,
                                      it.Element(xs + "titleOfClass").Value,
                                      it.Element(xs + "cusip").Value,
                                      int.Parse(it.Element(xs + "value").Value),
                                      int.Parse(it.Element(xs + "shrsOrPrnAmt").Element(xs + "sshPrnamt").Value),
                                      it.Element(xs + "shrsOrPrnAmt").Element(xs + "sshPrnamtType").Value);

    }
    static async Task<IEnumerable<Position>> LoadPositionsFromLinks(IEnumerable<Tuple<string, string>> dateLinks) {
        var posLists = new List<IEnumerable<Position>>();

        var tasks = dateLinks.Select(async dl => {
            var txt = await Utils.HttpLoadAsync(dl.Item2);
            posLists.Add(PositionsFromText(dl.Item1, txt));
        });

        var waitableTasks = tasks.ToArray();
        await Task.WhenAll(waitableTasks);

        return posLists.SelectMany(l => l);
    }

    public async static Task<GuruData> LoadPositions(string cik) {
        var invLinks = await Load13FLinksAsync(cik);
        var positions = await LoadPositionsFromLinks(invLinks.Item2);
        return new GuruData(invLinks.Item1, "", positions);
    }

}

