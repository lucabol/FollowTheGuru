using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;

public class Program {

    // Quick and dirty Console Printing
    static string cs(string s, int len) => s == null? "".PadRight(len,' ') : s.PadRight(len,' ');
    static string def(bool v, string s) => v ? s : "    ";

    static string DisplayPortToString(DisplayPortfolio dp) {
        var sb = new StringBuilder();
        sb.AppendLine(String.Join(" -- ", dp.DisplayName,dp.EndQuarterDate.ToString("d"), dp.TotalValue, dp.PositionsNumber));
        sb.AppendLine(String.Join(" ", "NEW ", "SOLD", cs("NAME", 40), cs("P/C", 5), cs("SHARES", 10),
                                    cs("VALUE", 10), "%PORT ", "CHANGE", "PRICE "));
        foreach (var p in dp.Positions) {
            sb.AppendLine(String.Join(" ", def(p.IsNew, "NEW "), def(p.IsSold,"SOLD"), cs(p.Name.Trim(),40), cs(p.PutCall,5) , cs(p.Shares.ToString(),10),
                                    cs(p.Value.ToString(),10), cs(Math.Round(p.PercOfPortfolio * 100,2).ToString(), 6),
                                    cs(Math.Round(p.Change * 100,2).ToString(),6), cs(Math.Round(p.Price,2).ToString(),6)));
        }
        return sb.ToString();
    }

    static string DisplayHistory(IEnumerable<DisplayCompany> companies) {
        var sb = new StringBuilder();
        foreach (var c in companies) {
            sb.AppendLine(String.Join(" ", cs(c.Name.Trim(), 40), cs(c.PutCall, 5)));
            foreach (var p in c.ChangePositions) {
                sb.AppendLine(String.Join(" ", "\t", def(p.IsNew, "NEW "), def(p.IsSold, "SOLD"), cs(p.Shares.ToString(), 10),
                            cs(p.Value.ToString(), 10), cs(Math.Round(p.PercOfPortfolio * 100, 2).ToString(), 6),
                            cs(Math.Round(p.Change * 100, 2).ToString(), 6), cs(Math.Round(p.Price, 2).ToString(), 6)));
            }
        }
        return sb.ToString();
    }

    // Try with the following ciks: 0001553733, 0001568820, 0001484148, 0001112520
    // or go to https://www.sec.gov/edgar/searchedgar/companysearch.html and put the name of the investor you are interested in
    public static void Main(string[] args)
    {
        var banner = "Usage: Follower Cik [-All]";
        if (args.Count() > 2) throw new Exception(banner);
        if (args.Count() == 2 && args[1] != "-All") throw new Exception(banner);

        if(args.Count() == 1) {
            var result = GuruLoader.FetchDisplayPortfolio(args[0]).Result;
            Console.WriteLine(DisplayPortToString(result));
        } else {
            // Printing Portfolio summary at both start and bottom
            var result = GuruLoader.FetchFullPortfolioData(args[0]).Result;
            Console.WriteLine(DisplayPortToString(result.Portfolio));
            Console.WriteLine(DisplayHistory(result.CompaniesHistory));
            Console.WriteLine(DisplayPortToString(result.Portfolio));
        }

    }
}
