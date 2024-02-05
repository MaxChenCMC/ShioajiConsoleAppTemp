using Newtonsoft.Json;
using Sinopac.Shioaji;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;


namespace Shioaji_SetQuoteCallback_PlaceOrder
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                SJ InitSJ = new();
                InitSJ.Initialize(@"D:\Sinopac.json", @"D:\Sinopac.pfx");

                InitSJ.TXFInspectAndPlaceOrder(10);
                /*============================================
                InitSJ.AmountRankSetQuoteCallback(15);

                InitSJ.ChangePercentRankAboveMonthlyAvg(20);
                InitSJ.Pnl("2024-01-10", "2024-01-10");
                ============================================*/
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }


        public class SJ
        {
            #region login exception handling. 路徑不會變的話沒必要寫這麼複雜
            private Shioaji _api;

            public void Initialize(string path1, string path2)
            {
                _api = new Shioaji();
                if (!File.Exists(path1) && !File.Exists(path2))
                {
                    throw new FileNotFoundException($"File not found: {path1}");
                    throw new FileNotFoundException($"File not found: {path2}");
                }

                try
                {
                    string jsonString = File.ReadAllText(path1);
                    JsonElement root = JsonDocument.Parse(jsonString).RootElement;
                    string apiKey = root.GetProperty("API_Key").GetString();
                    string secretKey = root.GetProperty("Secret_Key").GetString();
                    _api.Login(apiKey, secretKey);
                    _api.ca_activate(path2, root.GetProperty("ca_passwd").GetString(), root.GetProperty("person_id").GetString());
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to login to Shioaji API.", ex);
                }
            }
            #endregion


            #region general time setting. 判斷盤中SetQuoteCallback何時停止；若六日也要測試應自動換成最近一個交易日
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

            private DateTime GetCallbackEndTime()
            {
                DateTime DTN = DateTime.Now;
                bool preMktTime = DTN < DTN.Date + new TimeSpan(5, 00, 0);
                bool onMktTime = (DTN > DTN.Date + new TimeSpan(8, 45, 0) && DTN < DTN.Date + new TimeSpan(13, 45, 0));
                DateTime end = preMktTime ? DTN.Date + new TimeSpan(5, 00, 0) : (onMktTime ? DTN.Date + new TimeSpan(13, 45, 0) : DateTime.Now.Date + new TimeSpan(29, 0, 0));
                return end;
            }
            #endregion


            #region Feeling the prosperity or market by top n trade amount stocks. 盤中成交重心有N檔是追價成交，預期預高越可能推動指數
            public void AmountRankSetQuoteCallback(int TopN)
            {
                List<string> GetAmountRankCodes = _api.Scanners(scannerType: ScannerType.AmountRank, date: GetValidDate(), count: TopN)
                    .Select(x => (string)x.code)
                    .ToList();
                foreach (string code in GetAmountRankCodes)
                {
                    try
                    {
                        Exchange exchange = _api.Contracts.Stocks["TSE"].ContainsKey(code) ? Exchange.TSE : Exchange.OTC;
                        _api.Subscribe(_api.Contracts.Stocks[exchange.ToString()][code], QuoteType.tick, version: QuoteVersion.v1);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to subscribe to stock {code}: {ex.Message}");
                    }
                }
                _api.SetQuoteCallback_v1(myQuoteCB_v1);
            }


            List<dynamic> _temp = new List<dynamic>();
            void myQuoteCB_v1(Exchange exchange, dynamic tick)
            {
                var rec = new
                {
                    ts = DateTime.ParseExact(tick.datetime, "yyyy/MM/dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture)
                        .ToString("HH:mm:ss"),
                    code = tick.code,
                    tick_type = tick.tick_type,
                    close = tick.close,
                    total_amount = Math.Round(tick.total_amount / 100_000_000, 2),
                    sentiment = Math.Round((tick.close - tick.low) / (tick.high - tick.low) * 100, 2),
                };
                _temp.Add(rec);

                var data = _temp.GroupBy(item => item.code)
                    .Select(group => group.Last())
                    .OrderByDescending(item => item.total_amount)
                    .ToList();

                Console.WriteLine(string.Join("\n", data));
                Console.WriteLine($"AmountRank top 15, {data.Count(x => x.tick_type == 1)} stocks traded at ASK price");
                Console.WriteLine($"Sentiment { (int)data.Average(x => (decimal)x.sentiment)}%");
                Console.WriteLine("======================================================================================================");
            }
            #endregion


            #region depends on pnl of position to break or not. 有部位就依成本價「移動停利、保本or停損」，若無部位就建新倉後依據前面3種情境出場
            public void TXFInspectAndPlaceOrder(double StopLossMargin)
            {
                List<double> SwingHighs = new List<double>();
                _api.Subscribe(_api.Contracts.Futures["TXF"]["TXFR1"], QuoteType.tick, version: QuoteVersion.v1);
                void TXFR1QuoteCB(Exchange exchange, dynamic tick)
                {
                    SwingHighs.Add((double)tick.close);
                    Console.WriteLine($"目前記了{SwingHighs.Count()}筆，最高點{SwingHighs.Max()}，最新報價{SwingHighs.Last()}");
                }
                _api.SetQuoteCallback_v1(TXFR1QuoteCB);


                while (DateTime.Now < GetCallbackEndTime())
                {
                    Console.WriteLine($"放著不管的話預計{GetCallbackEndTime()}退出");
                    List<Sinopac.Shioaji.FuturePosition> _src = _api.ListPositions(_api.FutureAccount);
                    if (_src.Any(x => x.code.Contains("MXF")))
                    {
                        double EntryPrice = (double)_src.Where(x => x.code.Contains("MXF")).Select(x => x.price).FirstOrDefault();
                        if (SwingHighs.Max() >= EntryPrice + StopLossMargin)
                        {
                            EntryPrice += 2;
                            Console.WriteLine($"已拉開故到{EntryPrice}才會被掃出場");

                            if (SwingHighs.Last() <= (SwingHighs.Max() - StopLossMargin))
                            {
                                //視部位反向沖掉，掛在SwingHighs.Last() ☛ 成交回報
                                break;
                            }
                            else if (SwingHighs.Last() <= EntryPrice)
                            {
                                //視部位反向沖掉，掛在SwingHighs.Last() ☛ 成交回報
                                break;
                            }
                            else
                            {
                                Console.WriteLine("既沒拉回，也沒保本，硬撐中");
                            }
                        }
                        else if (SwingHighs.Last() < EntryPrice - StopLossMargin)
                        {
                            //視部位反向沖掉，掛在SwingHighs.Last() ☛ 成交回報
                            break;
                        }
                        else
                        {
                            Console.WriteLine("既沒拉開，也沒停損，硬撐中");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"沒有小台的部位");
                        // MXFPlaceOrder("MXF202402", 17000, true)
                        // 確定成交後再回來迴圈
                    }
                    Thread.Sleep(30_000);
                    //Console.WriteLine("小台積期：" + _api.Snapshots(new List<IContract>() { _api.Contracts.Futures["QFF"]["QFFR1"] })[0].close);
                }
            }


            public void MXFPlaceOrder(string mxfCode, int placePrice, bool isPrintResult)//, bool isIntraday
            {
                var _contract = _api.Contracts.Futures["MXF"][mxfCode];
                var _futOptOrder = new FutOptOrder()
                {
                    action = "Buy",
                    price = placePrice,
                    quantity = 1,
                    price_type = "LMT",
                    order_type = "ROD",
                    //octype = isIntraday ? "DayTrade" : "",
                };
                var _trade = _api.PlaceOrder(_contract, _futOptOrder);

                if (isPrintResult)
                {
                    var ret = new
                    {
                        //要把原廠Log改成false不然很煩
                        status = _trade.status.status,
                        price = _trade.order.price,
                        deal_quantity = _trade.status.deal_quantity,
                        cancel_quantity = _trade.status.cancel_quantity,
                        action = _trade.order.action,
                        //order_lot = _trade.order.order_lot,
                        //quantity = _trade.order.quantity,
                        //seqno = _trade.order.seqno,  // 234925
                    };
                    Console.WriteLine("委託細節" + string.Join(", ", ret));
                }
            }
            #endregion


            #region !!強勢股
            public List<dynamic> ChangePercentRankAboveMonthlyAvg(int TopN)
            {
                List<string> GetChangePercentRankCodes = _api.Scanners(scannerType: ScannerType.ChangePercentRank, date: GetValidDate(), count: TopN)
                    .Where(x => x.close > 20 && x.total_amount > 500_000_000)
                    .Select(x => (string)x.code)
                    .ToList();

                // 從openapi抓 List<string> 股票們存成 Dictionary<string, List<object>> ☛ { 股號: [股名, 月均價]}
                var client = new HttpClient();
                var response = client.GetAsync("https://openapi.twse.com.tw/v1/" + "exchangeReport/STOCK_DAY_AVG_ALL").Result; // 上市個股日收盤價及月平均價
                var json = response.Content.ReadAsStringAsync().Result;
                List<dynamic> OpenApiItems = JsonConvert.DeserializeObject<List<dynamic>>(json);

                List<dynamic> _temp = new List<dynamic>();
                foreach (var item in OpenApiItems.Where(x => GetChangePercentRankCodes.Contains(x.Code.ToString())))
                {
                    // openapi 目前只抓twse故不做OTC判斷
                    var rec = new
                    {
                        Code = item.Code,
                        Name = item.Name,
                        MonthlyAveragePrice = item.MonthlyAveragePrice,
                    };
                    _temp.Add(rec);
                }

                var obj_IContracts = new List<IContract>();
                foreach (var i in _temp.Select(x => x.Code).ToList())
                {
                    obj_IContracts.Add(_api.Contracts.Stocks["TSE"][i]);
                }

                List<dynamic> _temp1 = new List<dynamic>();
                foreach (var i in _api.Snapshots(obj_IContracts))
                {
                    var rec = new
                    {
                        Code = i.code,
                        close = i.close,
                        change_rate = i.change_rate,
                        total_amount = i.total_amount,
                    };
                    _temp1.Add(rec);
                }
                Console.WriteLine(string.Join("\n", _temp));
                Console.WriteLine(_temp.Select(x => x.Code).ToList());
                Console.WriteLine(string.Join("\n", _temp1));
                return null;
            }
            #endregion


            #region History profit and loss log. 歷史損益API雖設計支援日期區間，但現行日期區間多日會報錯entry_price不可為{null}
            public void Pnl(string bgn, string end)
            {
                try
                {
                    var src = _api.ListProfitLossSummary(bgn, end, _api.FutureAccount).profitloss_summary[0];
                    var _Pnl = new
                    {
                        code = (src.code.Substring(3, 5)) + (src.code[8] < 'L' ? "Call" : "Put"),
                        direction = src.direction,
                        entry_price = src.entry_price,
                        cover_price = src.cover_price,
                        netPnl = src.pnl - src.fee - src.tax
                    };
                    Console.WriteLine(string.Join("\n", _Pnl));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed {ex.Message}");
                }
            }
            #endregion

        }
    }
}