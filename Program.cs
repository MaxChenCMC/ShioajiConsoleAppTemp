using Sinopac.Shioaji;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;


namespace Shioaji_SetQuoteCallback_PlaceOrder
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                SJ InitSJ = new();
                InitSJ.Initialize();
                InitSJ.AmountRankSetQuoteCallback(15);
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }


        public class SJ
        {
            #region seperate login method from class initialize so the login status can keeped.
            private Shioaji _api;

            public void Initialize()
            {
                _api = new Shioaji();
                string path = @"D:\Sinopac.json";
                Login(path);
            }

            private void Login(string path)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"壹! File not found: {path}");
                }

                try
                {
                    string jsonString = File.ReadAllText(path);
                    JsonElement root = JsonDocument.Parse(jsonString).RootElement;
                    string apiKey = root.GetProperty("API_Key").GetString();
                    string secretKey = root.GetProperty("Secret_Key").GetString();
                    _api.Login(apiKey, secretKey);
                }
                catch (Exception ex)
                {
                    throw new Exception("貮! Failed to login to Shioaji API.", ex);
                }
            }
            #endregion


            #region dateformate, amt rank code to ist, subscibe,
            private string GetValidDate()
            {
                DateTime date = DateTime.Now.DayOfWeek switch
                {
                    DayOfWeek.Saturday => DateTime.Now.AddDays(-1),
                    DayOfWeek.Sunday => DateTime.Now.AddDays(-2),
                    _ => DateTime.Now
                };
                return date.ToString("yyyy-MM-dd");
            }


            public void AmountRankSetQuoteCallback(int TopN)
            {
                List<string> GetAmountRankCodes = _api.Scanners(scannerType: ScannerType.AmountRank, date: GetValidDate(), count: TopN)
                    .Select(x => (string)x.code)
                    .ToList();
                foreach (string code in GetAmountRankCodes)
                {
                    try
                    {
                        Exchange exchange = _api.Contracts.Stocks["TSE"].ContainsKey(code) ? "TSE" : "OTC";
                        _api.Subscribe(_api.Contracts.Stocks[exchange.ToString()][code], QuoteType.tick, version: QuoteVersion.v1);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to subscribe to stock {code}: {ex.Message}");
                    }
                }

                // need to regulate format in advanced
                _api.SetQuoteCallback_v1(myQuoteCB_v1);
            }


            //List<dynamic> _temp = new List<dynamic>();
            void myQuoteCB_v1(Exchange exchange, dynamic tick)
            {
                // _若放這會不會一直被洗掉?無法累積齊TopN？若執行超多次不重複股票仍兩三檔而已那就是_temp不該放這 而是該放myQuoteCB_v1同層
                List<dynamic> _temp = new List<dynamic>();

                var rec = new
                {
                    ts = DateTime.ParseExact(tick.datetime, "yyyy/MM/dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture)
                        .ToString("HH:mm:ss"),
                    code = tick.code,
                    tick_type = tick.tick_type,
                    close = tick.close,
                    total_amount = tick.total_amount / 100_000_000,
                };
                _temp.Add(rec);

                var data = _temp.GroupBy(item => item.code)
                    .Select(group => group.Last())
                    .OrderByDescending(item => item.total_amount)
                    .ToList();

                Console.WriteLine(string.Join("\n", data));
                Console.WriteLine($"Todays focus {data.Count(x => x.tick_type == 1)} dealed at ask price");
                Console.WriteLine("======================================================================================");
            }
            #endregion
        }
    }
}
