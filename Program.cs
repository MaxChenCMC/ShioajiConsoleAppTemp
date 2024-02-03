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
                    throw new FileNotFoundException($"File not found: {path}");
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
                    throw new Exception("Failed to login to Shioaji API.", ex);
                }
            }
            #endregion


            #region Today trade Amount Rank top N. Count how many stocks are dealed at ask price.
            public void AmountRankSetQuoteCallback(int argCount)
            {
                string argDate;
                if (DateTime.Now.DayOfWeek.ToString() == "Saturday") argDate = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
                else if (DateTime.Now.DayOfWeek.ToString() == "Sunday") argDate = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");
                else argDate = DateTime.Now.ToString("yyyy-MM-dd");
                List<string> tg = _api.Scanners(scannerType: ScannerType.AmountRank, date: argDate, count: argCount).Select(x => (string)x.code).ToList();
                foreach (string i in tg)
                {
                    try
                    {
                        _api.Subscribe(_api.Contracts.Stocks["TSE"][i], QuoteType.tick, version: QuoteVersion.v1);
                    }
                    catch (Exception ex)
                    {
                        _api.Subscribe(_api.Contracts.Stocks["OTC"][i], QuoteType.tick, version: QuoteVersion.v1);
                    }
                }
                List<dynamic> _temp = new List<dynamic>();
                void myQuoteCB_v1(Exchange exchange, dynamic tick)
                {
                    var rec = new
                    {
                        ts = DateTime.ParseExact(tick.datetime,
                                             "yyyy/MM/dd HH:mm:ss.ffffff",
                                             System.Globalization.CultureInfo.InvariantCulture)
                                             .ToString("HH:mm:ss"),
                        code = tick.code,
                        tick_type = tick.tick_type,
                        close = tick.close,
                        total_amount = tick.total_amount / 100_000_000,
                    };
                    _temp.Add(rec);
                    var data = _temp.GroupBy(item => item.code).Select(group => group.Last()).OrderByDescending(item => item.total_amount).ToList();
                    Console.WriteLine(string.Join("\n", data));
                    Console.WriteLine($"Todays focus {data.Count(x => x.tick_type == 1)}/{argCount} dealed at ask price");
                    Console.WriteLine("======================================================================================");
                }
                _api.SetQuoteCallback_v1(myQuoteCB_v1);
            }
            #endregion
        }
    }
}