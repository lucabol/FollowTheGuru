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
        sb.AppendLine(String.Join(" ", "NEW ", "SOLD", cs("NAME", 30), cs("P/C", 5), cs("SHARES", 10),
                                    cs("VALUE", 10), "%PORT", "CHANGE"));
        foreach (var p in dp.Positions) {
            sb.AppendLine(String.Join(" ", def(p.IsNew, "NEW "), def(p.IsSold,"SOLD"), cs(p.Name.Trim(),30), cs(p.PutCall,5) , cs(p.Shares.ToString(),10),
                                    cs(p.Value.ToString(),10), Math.Round(p.PercOfPortfolio * 100,2), Math.Round(p.Change * 100,2)));
        }
        return sb.ToString();
    }

    public static void Main(string[] args)
    {
        if (args.Count() != 1) throw new Exception("Usage: Follower Cik");
        var result =  GuruLoader.FetchDisplayPortfolio(args[0]).Result;
        Console.WriteLine(DisplayPortToString(result));
    }
}
