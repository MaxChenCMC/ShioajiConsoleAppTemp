using Newtonsoft.Json;
using Sinopac.Shioaji;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace ShioajiConsoleApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            SJ InitSJ = new();
            InitSJ.Login(@"D:\Sinopac.json");
            //==================================================================
            //InitSJ.testCallBack();
            InitSJ.AmountRankSetQuoteCallback();

            //foreach (var i in InitSJ.ScannersChangePercentRank(50))
            //{
            //    i.Value.ForEach(Console.WriteLine);
            //    Console.WriteLine();
            //}


            //==================================================================
            Console.ReadLine();
        }


        public class SJ
        {
            #region 登入沒事不會改
            private static Shioaji _api = new Shioaji();

            public void Login(string path)
            {
                string jsonString = File.ReadAllText(path);
                JsonElement root = JsonDocument.Parse(jsonString).RootElement;
                _api.Login(root.GetProperty("API_Key").GetString(), root.GetProperty("Secret_Key").GetString());
            }
            #endregion


            #region 即時報價 testCallBack()
            public void testCallBack()
            {
                _api.Subscribe(_api.Contracts.Futures["TXF"]["TXFR1"], QuoteType.tick, version: QuoteVersion.v1);
                _api.SetQuoteCallback_v1(myQuoteCB_v1);
            }

            List<dynamic> _temp = new List<dynamic>();
            private void myQuoteCB_v1(Exchange exchange, dynamic tick)
            {
                var rec = new
                {
                    ts = DateTime.ParseExact(tick.datetime,
                                             "yyyy/MM/dd HH:mm:ss.ffffff",
                                             System.Globalization.CultureInfo.InvariantCulture)
                                             .ToString("HH:mm:ss"),
                    close = tick.close,
                    tick_type = tick.tick_type,  // 1內、2外
                    chg_type = tick.chg_type,    // 2漲、3平、4跌
                    //code = tick.code, pct_chg = tick.pct_chg, underlying_price = tick.underlying_price,
                };
                _temp.Add(rec);

                if (_temp.Last().close >= 17995)
                {
                    Console.WriteLine("條件達成。似乎在myQuoteCB_v1裡只要一有資料更新就會觸發一次");
                }
                else
                {
                    Console.WriteLine(rec);
                    Console.WriteLine(_temp.Max(x => x.GetType().GetProperty("close").GetValue(x)));
                }
            }
            #endregion


            #region 模擬移停/保本/停損
            public void MxfMock(double argEntryPrice, double argStp)
            {
                List<double> _temp = new List<double>();
                while (true)
                {
                    double spotClose = (double)_api.Snapshots(new List<IContract>() { _api.Contracts.Futures["TXF"]["TXFR1"] })[0].close;
                    _temp.Add(spotClose);
                    _temp.Reverse();
                    string result = string.Join(", ", _temp.Take(10));
                    Console.WriteLine($"現在{spotClose}點 \t 過去 10 ticks {result}\n高點{_temp.Max()} since start");
                    if (spotClose >= argEntryPrice + argStp)
                    {
                        argEntryPrice += 2;
                        Console.WriteLine($"已拉開故到{argEntryPrice}才會被掃出場");
                        while (true)
                        {
                            spotClose = (double)_api.Snapshots(new List<IContract>() { _api.Contracts.Futures["TXF"]["TXFR1"] })[0].close;
                            _temp.Add(spotClose);
                            if (spotClose <= (_temp.Max() - argStp))
                            {
                                string desc = spotClose >= argEntryPrice ? "停利" : "保本";
                                Console.WriteLine($"★{desc}出在{spotClose}。");
                                break;
                            }
                            else if (spotClose <= argEntryPrice)
                            {
                                Console.WriteLine($"★保本出在{spotClose}。");
                                break;
                            }
                            else
                            {
                                Console.WriteLine(spotClose);
                                Thread.Sleep(5_000);
                            }
                        }
                        break;
                    }
                    else if (spotClose <= argEntryPrice - argStp)
                    {
                        Console.WriteLine($"★停損，出在{spotClose}。");
                        break;
                    }
                    else Thread.Sleep(5_000);
                }
                Console.WriteLine("★★小台積期：" + _api.Snapshots(new List<IContract>() { _api.Contracts.Futures["QFF"]["QFFR1"] })[0].close);
            }
            #endregion


            #region 強勢股  ScannersChangePercentRank()
            public Dictionary<string, List<object>> ScannersChangePercentRank(int argCnt)
            {
                string argDate;
                if (DateTime.Now.DayOfWeek.ToString() == "Saturday") argDate = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
                else if (DateTime.Now.DayOfWeek.ToString() == "Sunday") argDate = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");
                else argDate = DateTime.Now.ToString("yyyy-MM-dd");

                List<string> ls_sids = new List<string>();
                foreach (var i in _api.Scanners(scannerType: ScannerType.ChangePercentRank, date: argDate, count: argCnt)
                // Scanners漲幅榜用linq篩出 $20且過五億的標的 把Code存 List<string>
                .Where(x => x.close > 20 && x.total_amount > 500_000_000))
                {
                    ls_sids.Add(i.code);
                }

                // 從openapi抓 List<string> 股票們存成 Dictionary<string, List<object>> ☛ { 股號: [股名, 月均價]}
                var client = new HttpClient();
                var response = client.GetAsync("https://openapi.twse.com.tw/v1/" + "exchangeReport/STOCK_DAY_AVG_ALL").Result; // 上市個股日收盤價及月平均價
                var json = response.Content.ReadAsStringAsync().Result;
                List<dynamic> src = JsonConvert.DeserializeObject<List<dynamic>>(json);
                Dictionary<string, List<object>> retDict = new Dictionary<string, List<object>>();
                foreach (var i in src.Where(x => ls_sids.Contains(x.Code.ToString())))
                {
                    List<object> _retDict = new List<object>();
                    _retDict.Add(i.Name.ToString());
                    _retDict.Add((Double)i.MonthlyAveragePrice);
                    retDict.Add(i.Code.ToString(), _retDict);
                }


                // 同格式的Dictionary<string, List<object>> 另用Snapshots對同群標的做 { 股號: [即時價, 漲額, 成交值億]}
                var obj_IContract = new List<IContract>();
                foreach (var i in retDict.Keys)
                    try { obj_IContract.Add(_api.Contracts.Stocks["TSE"][i]); }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"宣告{ex}但沒用到會報alert");
                        obj_IContract.Add(_api.Contracts.Stocks["OTC"][i]);
                    }

                Dictionary<string, List<object>> retDict1 = new Dictionary<string, List<object>>();
                foreach (var i in _api.Snapshots(obj_IContract))
                {
                    List<object> _retDict1 = new List<object>();
                    _retDict1.Add(i.close);
                    _retDict1.Add(i.change_rate);
                    _retDict1.Add(Math.Round(i.total_amount / 100_000_000d, 2));
                    retDict1.Add(i.code, _retDict1);
                }


                // 把兩個 Dictionary<string, List<object>>併起來，再補上乖離 ☛ { 股號:         [股名, 月均價, 即時價, 漲幅, 成交值億, 距月均價多遠]}
                // 這個Concat的dict留到react定義 interface                  ☛  [key: string]: [string, number, number, number, number, number];
                var ret = retDict.Concat(retDict1).GroupBy(kvp => kvp.Key).ToDictionary(g => g.Key, g => g.SelectMany(kvp => kvp.Value).ToList());
                foreach (var i in ret.Keys)
                {
                    ret[i].Add(Math.Round(
                        ((double)ret[i][2] / (double)ret[i][1]) - 1
                        , 2));
                }

                // 篩漲超過月線x%的
                return ret.Where(x => (Double)x.Value[5] >= 0.00).ToDictionary(x => x.Key, x => x.Value);
            }
            #endregion


            #region 溫度計
            public void AmountRankSetQuoteCallback()
            {
                string argDate;
                if (DateTime.Now.DayOfWeek.ToString() == "Saturday") argDate = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
                else if (DateTime.Now.DayOfWeek.ToString() == "Sunday") argDate = DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd");
                else argDate = DateTime.Now.ToString("yyyy-MM-dd");
                List<string> tg = _api.Scanners(scannerType: ScannerType.AmountRank, date: argDate, count: 5).Select(x => (string)x.code).ToList();
                foreach (string i in tg)
                {
                    _api.Subscribe(_api.Contracts.Stocks["TSE"][i], QuoteType.tick, version: QuoteVersion.v1);
                }

                //List<string> abc = new List<string> { "TXFR1", "QFFR1" };
                //foreach(var i in abc)
                //{
                //    _api.Subscribe(_api.Contracts.Futures[i.Substring(0,3)][i], QuoteType.tick, version: QuoteVersion.v1);
                //}
                //_api.Subscribe(_api.Contracts.Futures["TXF"]["TXFR1"], QuoteType.tick, version: QuoteVersion.v1); //TXFB4

                _api.Subscribe(_api.Contracts.Futures["QFF"]["QFFR1"], QuoteType.tick, version: QuoteVersion.v1); //QFFB4

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
                        tick_type = tick.tick_type,  // 1內、2外 ☛好像官方文件寫相反了
                        chg_type = tick.chg_type,    // 2漲、3平、4跌 ☛ 好像只是相對昨收今平，而不是tick的即時狀況
                    };
                    _temp.Add(rec); 
                    Console.WriteLine(_temp.Where(x => x.code.Contains("QFF")).Select(x => x).LastOrDefault());
                    Console.WriteLine("======================================================================");
                    Console.WriteLine(_temp.Where(x => x.code != "").Select(x => x));
                }
                _api.SetQuoteCallback_v1(myQuoteCB_v1);

                //dynamic res = _temp.Where(x => x.GetType().GetProperty("code").GetValue(x) == "TXFB4").Last();
                //var a = res.code;
                //var b = res.tick_type;
                //var c = res.chg_type;
                //dynamic res1 = _temp.Where(x => x.GetType().GetProperty("code").GetValue(x) == "QFFB4").Last();
                //var d = res1.code;
                //var e = res1.tick_type;
                //var f = res1.chg_type;
                //string result = string.Join(", ", (a, b, c), (d, e, f));
                //Console.WriteLine(result);
            }
            #endregion

        }
    }
}
