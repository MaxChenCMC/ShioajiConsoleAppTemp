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
            InitSJ.Login();

            //==================================================================
            //InitSJ.testCallBack();


            foreach (var i in InitSJ.ScannersChangePercentRank(50))
            {
                i.Value.ForEach(Console.WriteLine);
                Console.WriteLine();
            }


            //InitSJ.MxfMock( 180_45, 5);
            //==================================================================
            Console.ReadLine();
        }


        public class SJ
        {
            #region 登入沒事不會改
            private static Shioaji _api = new Shioaji();

            public void Login()
            {
                string jsonString = File.ReadAllText(@"D:\Sinopac.json");
                JsonElement root = JsonDocument.Parse(jsonString).RootElement;
                _api.Login(root.GetProperty("API_Key").GetString(), root.GetProperty("Secret_Key").GetString());
            }
            #endregion





            #region 模擬移停/保本/停損
            public void MxfMock(int argEntryPrice, int argStp)
            {
                List<double> _temp = new List<double>();
                while (DateTime.Now < new DateTime(2024, 1, 27, 5, 0, 0))
                {
                    List<FuturePosition> rtPositions = _api.ListPositions(_api.FutureAccount);
                    var spotClose = _api.Snapshots(new List<IContract>() { _api.Contracts.Futures["TXF"]["TXFR1"] })[0].close;
                    if (true)
                    {
                        _temp.Add(spotClose);
                        _temp.Reverse();
                        string result = string.Join(", ", _temp.Take(10));
                        Console.WriteLine($"現在{spotClose}點 \t 過去 10 ticks {result}\nMax is {_temp.Max()} since start");

                        if ((decimal)spotClose >= argEntryPrice + argStp)
                        {
                            argEntryPrice += 2;
                            Console.WriteLine($"已拉開故到{argEntryPrice}才會被掃出場");

                            //? 要用=因為賭高點會繼創高，若僅用<=就會馬上被掃出場，且怕漏add故同時要or <= argEntryPrice
                            if (spotClose == (_temp.Max() - argStp) || spotClose <= argEntryPrice)
                            {
                                Console.WriteLine($"這裡斷得很莫名");
                                break;
                            }
                            else if ((decimal)spotClose <= argEntryPrice)
                            {
                                //Console.WriteLine($"{}");
                                break;
                            }
                            string desc = spotClose >= argEntryPrice ? "停利" : "保本";
                            Console.WriteLine($"★{desc}出在{spotClose}。");
                            break;  //這裡必加不然都已停利or保本了卻還繼續if
                        }
                        else if ((decimal)spotClose <= argEntryPrice - argStp)
                        {
                            Console.WriteLine($"★停損，出在{spotClose}。");
                            break;
                        }
                        Thread.Sleep(5_000);
                    }
                }
                Console.WriteLine("★★小台積期：" + _api.Snapshots(new List<IContract>() { _api.Contracts.Futures["QFF"]["QFFR1"] })[0].close);
            }
            #endregion


            #region mxf是否有部位來決定移停/保本/停損
            public void MxfRealtime(int stp)
            {
                List<double> _temp = new List<double>();
                while (DateTime.Now < new DateTime(2024, 1, 27, 5, 0, 0))
                {
                    //宣告：庫存、當下報價、成本價
                    List<FuturePosition> rtPositions = _api.ListPositions(_api.FutureAccount);
                    double spotClose = _api.Snapshots(new List<IContract>() { _api.Contracts.Futures["TXF"]["TXFR1"] })[0].close;
                    bool cond = rtPositions.Any(x => x.code.Contains("MXF"));
                    var entry_price = rtPositions.Where(x => x.code.Contains("MXF")).Select(x => x.price).FirstOrDefault();

                    // 有部位 && 未停損arg
                    //if (cond && rtPositions.Where(x => x.code.Contains("MXF")).Select(x => x.pnl).FirstOrDefault() >= (-50 * stp))
                    if (true)
                    {
                        _temp.Add(spotClose);
                        _temp.Reverse();
                        string result = string.Join(", ", _temp.Take(10));
                        Console.WriteLine($"現在{spotClose}點\n過去 10 ticks {result}");

                        int MOCKentry_price = 18030;

                        // 若漲的有感就保本
                        if ((decimal)spotClose > MOCKentry_price + 12)
                        {
                            MOCKentry_price += 2;  //entry_price += 2;
                            Console.WriteLine($"已拉開安全邊際故跳出迴圈的條件已改成{MOCKentry_price}保本出場");

                            // 膽小版自高點拉回10就出場
                            if ((decimal)spotClose <= MOCKentry_price)  //if ((decimal)spotClose <= entry_price)
                            {
                                Console.WriteLine($"現在{spotClose}，已從swing high {_temp.Max()}拉回10點");
                                //賣出函式
                                break;
                            }
                            // 打到保本就停止
                            else if (spotClose <= _temp.Max() - 10)
                            {
                                Console.WriteLine($"打到保本因為現在{spotClose}，已低於{MOCKentry_price}");
                                //賣出函式
                                break;
                            }
                        }
                        // 單純看現價比成本低 arg 點就砍
                        else if ((decimal)spotClose <= MOCKentry_price - stp)
                        {
                            //賣出函式
                            break;
                        }

                        Thread.Sleep(5_000);
                        // 一旦break都該傳個pnl summary比較詳細
                    }

                    else if (cond == false)
                    {
                        Console.WriteLine($"因沒mxf部位而結束。現在{spotClose}點");
                        break;
                    }
                }
                Console.WriteLine("小台積期：" + _api.Snapshots(new List<IContract>() { _api.Contracts.Futures["QFF"]["QFFR1"] })[0].close);
            }
            #endregion


            #region 即時報價 testCallBack()
            public void testCallBack()
            {
                _api.Subscribe(_api.Contracts.Futures["TXF"]["TXFR1"], QuoteType.tick, version: QuoteVersion.v1);
                //_api.Subscribe(_api.Contracts.Futures["QFF"]["QFFR1"], QuoteType.tick, version: QuoteVersion.v1);
                _api.SetQuoteCallback_v1(myQuoteCB_v1);
            }
            private static void myQuoteCB_v1(Exchange exchange, dynamic tick)
            {
                //tick_type 1內、2外
                //chg_type  2漲、3平、4跌
                Console.WriteLine(tick.code + "\t" + tick.datetime + "\t" + tick.underlying_price + "\t" + tick.close + "\t" + tick.tick_type + "\t" + tick.price_chg + "\t" + tick.pct_chg);
                Thread.Sleep(3000);
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
        }
    }
}
