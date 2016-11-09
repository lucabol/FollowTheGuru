using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;


/*
 * This file contains utility functions to parse SEC filings to get out a portfolio at a particular date for a particular investor.
 *  Investors are identified by a CIK
 *  Given a CIK, you can get to an RSS feed that contains all filing for that investor
 *  Given the RSS feed, you can scan it to get links for all the 13F filings that represent the portfolio at a particular date.
 *  Given a link to a 13F filing, you need to scan the resulting HTML file for a link to the full submission file
 *  Given the full submission file, you need to scan it to extract the entries for each position in the portfolio
 *  Given the last two portfolios, you can then diff them to find the changes and return a summary of the portfolio and changes
 * The code below implements the above workflow as granular functions so that can be reused in different architecture (i.e. one actor for each portfolio
 *   coarse grain web service, other)
 */

// A position as represented in the SEC filing (not good for direct display)
public class Position {

    public string Name { get; set; }
    public string ClassTitle { get; set; }
    public string Cusip { get; set; }
    public int Value { get; set; }
    public int Shares { get; set; }
    public string SharesType { get; set; }

    public string PutCall { get; set; }

}

// A portfolio as represented in the SEC filing
public class Portfolio {

    public int TotalValue { get; set; }
    public int PositionsNumber { get; set; }
    public DateTime EndQuarterDate { get; set; }
    public IEnumerable<Position> Positions { get; set; }
}

// Representation of the RSS file containing links to all 13F filings
public class RssData {
    public string DisplayName { get; set; }
    public IEnumerable<string> Links { get; set; }
}

// A position in a display friendly form (i.e. directly displayable)
// It adds to what is in the SEC filings data about changes in positions and percentage of portfolio
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

// A displayable portfolio
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
    public static string ComposeGuruUrl(string cik)
        => $"https://www.sec.gov/cgi-bin/browse-edgar?action=getcompany&CIK={cik}&CIK=0001568820&type=&dateb=&owner=exclude&start=0&count=40&output=atom";
    // Links in the submission file happen to be relative, this absolutes them. Brittle, but if the below change, the world might come to end
    // as everybody and his brother has links to it
    public static string MakeSecLinkAbsolute(string relUrl) => $"https://www.sec.gov{relUrl}";

    // The submission file is divided in two parts, the first contains investor related data, the second the portfolio for that date
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

    // gets a portfolio out of a submission file
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

    // Gets all positions stored in the xml portfolio part of the xml file
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

    // Enriches the position type for easy display
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

    // Uniquely identify the position
    static string FormKey(Position p) {
        return p.Cusip + p.ClassTitle + p.PutCall;
    }

    // Diffs two portfolios and figure out what changed, this could perhaps be written more functionally
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

        // Eye candy
        var sortedPos = positions.OrderByDescending(pos => pos.Value);

        return new DisplayPortfolio() {
            DisplayName = displayName,
            EndQuarterDate = newPort.EndQuarterDate,
            TotalValue = newPort.TotalValue,
            PositionsNumber = newPort.PositionsNumber,
            Positions = sortedPos
        };
    }

    // Utility function to help waiting for all portfolios to be loaded
    async static Task<Portfolio> FetchPortfolio(HttpClient client, string htmlLink) {
        using (var html = await client.GetStreamAsync(htmlLink)) {
            var submissionLink = ParseHtmFile(html);
            using (var submissionStream = await client.GetStreamAsync(MakeSecLinkAbsolute(submissionLink))) {
                return ParseSubmissionFile(submissionStream);
            }
        }
    }
    public async static Task<DisplayPortfolio> FetchDisplayPortfolio(string cik) {
        if (String.IsNullOrEmpty(cik)) throw new Exception("Cik cannot be empty");

        var rssUrl = ComposeGuruUrl(cik);
        using (var client = new HttpClient()) {
            // Getting the rss stream cannot be started in parallel as it needs to be read before loading the portfolios
            var rss = await client.GetStreamAsync(rssUrl);
            var rssData = ParseRssText(rss);

            var portsNumber = rssData.Links.Count();
            if (portsNumber == 0) throw new Exception("No portfolios for this cik");

            var port1 = FetchPortfolio(client, rssData.Links.First());

            // If there is just one portfolio (i.e. investor just started investing) create an empty old one so that the logic
            // populates a displayPortfolio where all the positions are marked as new
            var port2 = portsNumber == 1 ? Task.FromResult(new Portfolio() { Positions = new Position[] { } }) : FetchPortfolio(client, rssData.Links.ElementAt(1));

            var portfolios = await Task.WhenAll(new Task<Portfolio>[] { port1, port2 });

            return CreateDisplayPortfolio(rssData.DisplayName, portfolios[0], portfolios[1]);
        }
    }
}

