using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

public class Position {

    public string Name { get; set; }
    public string ClassTitle { get; set; }
    public string Cusip { get; set; }
    public int Value { get; set; }
    public int Shares { get; set; }
    public string SharesType { get; set; }

    public string PutCall { get; set; }

}

public class Portfolio {

    public int TotalValue { get; set; }
    public int PositionsNumber { get; set; }
    public DateTime EndQuarterDate { get; set; }
    public IEnumerable<Position> Positions { get; set; }
}

public class RssData {
    public string DisplayName { get; set; }
    public IEnumerable<string> Links { get; set; }
}

public class DisplayPosition {
    public string Name { get; set; }
    public string ClassTitle { get; set; }
    public string Cusip { get; set; }
    public int Value { get; set; }
    public int Shares { get; set; }
    public string PutCall { get; set; }
    public double Change { get; set; }
    public double PercOfPortfolio { get; set; }
    public bool IsNew { get; set; }
    public bool IsSold { get; set; }
}

public class DisplayPortfolio {

    public string DisplayName { get; set; }
    public int TotalValue { get; set; }
    public int PositionsNumber { get; set; }
    public DateTime EndQuarterDate { get; set; }
    public IEnumerable<DisplayPosition> Positions { get; set; }
}

public static class GuruLoader {
    // Parse the rss stream into the displayable name of the guru and the links
    // to the html file pointing to data on the portfolio
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

    // Parse the html file pointed to by the rss stream to extract the name of the text file
    // containing date of filing, date of portfolio and positions
    public static string ParseHtmFile(Stream htmlStream) {
        var html = new HtmlDocument();
        html.Load(htmlStream);
        return html.DocumentNode.Descendants("tr")
                      .Where(tr => tr.Descendants("td").Any(td => td.InnerText == "Complete submission text file"))
                      .Single()
                      .Descendants("a")
                      .Single()
                      .GetAttributeValue("href", "NO LINK FOUND");
    }

    // Given CIK, gets the link for the RSS file
    public static string ComposeGuruUrl(string cik) => $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={cik}&CIK=0001568820&type=&dateb=&owner=exclude&start=0&count=40&output=atom";

    static Tuple<XDocument, XDocument> SplitSubmissionFile(Stream submissionFile) {
        using (var reader = new StreamReader(submissionFile, Encoding.UTF8)) {
            var fullTxt = reader.ReadToEnd();
            var startFirstDoc = fullTxt.IndexOf("<?xml");
            var endFirstDoc = fullTxt.IndexOf("</XML>");
            var firstDoc = fullTxt.Substring(startFirstDoc, endFirstDoc - startFirstDoc);
            var firstXml = XDocument.Parse(firstDoc);

            var startSecondDoc = fullTxt.IndexOf("<informationTable");
            var endSecondDoc = fullTxt.IndexOf("</informationTable>") + "</informationTable>".Length;
            var secondDoc = fullTxt.Substring(startSecondDoc, endSecondDoc - startSecondDoc);
            var secondXml = XDocument.Parse(secondDoc);
            return Tuple.Create(firstXml, secondXml);
        }
    }

    public static Portfolio ParseSubmissionFile(Stream submissionFile) {
        var xmls = SplitSubmissionFile(submissionFile);
        var firstXml = xmls.Item1;
        var secondXml = xmls.Item2;

        XNamespace xs = "http://www.sec.gov/edgar/thirteenffiler";
        var endQuarterDate = DateTime.Parse(firstXml.Descendants(xs + "reportCalendarOrQuarter").Single().Value);
        var totalValue = int.Parse(firstXml.Descendants(xs + "tableValueTotal").Single().Value);
        var positionsNumber = int.Parse(firstXml.Descendants(xs + "tableEntryTotal").Single().Value);

        XNamespace ns = "http://www.sec.gov/edgar/document/thirteenf/informationtable";

        var positions = PositionsFromXml(secondXml);

        return new Portfolio {
            EndQuarterDate = endQuarterDate,
            TotalValue = totalValue,
            PositionsNumber = positionsNumber,
            Positions = positions
        };

    }

    static IEnumerable<Position> PositionsFromXml(XDocument xml) {

        XNamespace xs = "http://www.sec.gov/edgar/document/thirteenf/informationtable";

        return from it in xml.Descendants(xs + "infoTable")
               select new Position {
                   Name = it.Element(xs + "nameOfIssuer").Value,
                   ClassTitle = it.Element(xs + "titleOfClass").Value,
                   Cusip = it.Element(xs + "cusip").Value,
                   Value = int.Parse(it.Element(xs + "value").Value),
                   Shares = int.Parse(it.Element(xs + "shrsOrPrnAmt").Element(xs + "sshPrnamt").Value),
                   SharesType = it.Element(xs + "shrsOrPrnAmt").Element(xs + "sshPrnamtType").Value,
                   PutCall = it.Element(xs + "putCall")?.Value
               };
    }

    static DisplayPosition DisplayFromPosition(Position p) {
        return new DisplayPosition() {
            Name = p.Name,
            ClassTitle = p.ClassTitle,
            Shares = p.Shares,
            Cusip = p.Cusip,
            Value = p.Value,
            PutCall = p.PutCall
        };
    }

    static string FormKey(Position p) {
        return p.Cusip + p.ClassTitle + p.PutCall;
    }

    public static DisplayPortfolio CreateDisplayPortfolio(string displayName, Portfolio newPort, Portfolio oldPort) {
        var positions = new List<DisplayPosition>();
        var oldPostions = new Dictionary<string, Position>();

        foreach (var po in oldPort.Positions) oldPostions.Add(FormKey(po), po);

        // Process existing positions
        foreach (var pn in newPort.Positions) {
            var dp = DisplayFromPosition(pn);
            dp.PercOfPortfolio = (double)dp.Value / (double)newPort.TotalValue;

            Position oldPos;
            if (oldPostions.TryGetValue(FormKey(pn), out oldPos)) {
                dp.Change = (double)dp.Shares / (double)oldPos.Shares - 1;
                dp.IsNew = false;
                oldPostions.Remove(FormKey(pn)); // remove all positions that are still there so it leaves just the ones that have been sold
            } else {
                dp.Change = 0; // new position, not there in the old portfolio
                dp.IsNew = true;
            }
            dp.IsSold = false;
            positions.Add(dp);
        }
        // Process sold positions
        foreach (var sold in oldPostions.Values) {
            var dp = DisplayFromPosition(sold);
            dp.Shares = 0;
            dp.Value = 0;
            dp.PercOfPortfolio = 0;
            dp.IsNew = false;
            dp.Change = -1;
            dp.IsSold = true;
            positions.Add(dp);
        }

        return new DisplayPortfolio() {
            DisplayName = displayName,
            EndQuarterDate = newPort.EndQuarterDate,
            TotalValue = newPort.TotalValue,
            PositionsNumber = newPort.PositionsNumber,
            Positions = positions
        };
    }
}

