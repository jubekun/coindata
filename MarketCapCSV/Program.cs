using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

// string.format
// https://msdn.microsoft.com/ja-jp/library/0c899ak8(v=vs.110).aspx
// async/task
// http://qiita.com/acple@github/items/8f63aacb13de9954c5da


namespace MarketCapCSV {


	class Program {

		//----------------//
		//-- パラメータ --//
		//----------------//

		static readonly int interval = 2000;	// webアクセスの間隔
		static readonly int coincount = 10;  // 何位までのコインを表示するか
		static readonly bool outputVolume = true;	// 取引量を出力するかどうか
		static readonly bool outputMarkCp = true;   // 市場規模を出力するかどうか
		static readonly string sep = "\t";  // セパレータ

		static readonly string startDateStr = "20130428";

		static readonly string outFileName = "coininfo.txt";


		//--------------//
		//-- メソッド --//
		//--------------//

		private static String ToCoinHistoricalDataUri(string coinExt) {
			// 例　https://coinmarketcap.com/currencies/bitcoin/historical-data/?start=20130428&end=20170925

			return "https://coinmarketcap.com/currencies/"
							+ coinExt
							+ "/historical-data/"
							+ "?start="
							+ startDateStr
							+ "&end="
							+ string.Format("{0:0000}", DateTime.Today.Year)
							+ string.Format("{0:00}", DateTime.Today.Month)
							+ string.Format("{0:00}", DateTime.Today.Day)
							;
		}

		private static Dictionary<DateTime, string>  ToDateDict(IEnumerable<string>[] dateListCollection) {
			var dict = new Dictionary<DateTime, string>();
			foreach (var keys in dateListCollection) {

				foreach (string key in keys) {
					// 例　May 06, 2013

					if (key.Length != 12) {   // ヘッダ行のときここに来る
						Debug.WriteLine("??? " + key);
						continue;
					}

					var monstr = key.Substring(0, 3);
					var daystr = key.Substring(4, 2);
					var yarstr = key.Substring(8, 4);

					int mon = 0;
					switch (monstr.ToLower()) {
						case "jan": mon = 1; break;
						case "feb": mon = 2; break;
						case "mar": mon = 3; break;
						case "apr": mon = 4; break;
						case "may": mon = 5; break;
						case "jun": mon = 6; break;
						case "jul": mon = 7; break;
						case "aug": mon = 8; break;
						case "sep": mon = 9; break;
						case "oct": mon = 10; break;
						case "nov": mon = 11; break;
						case "dec": mon = 12; break;
						default: Debug.WriteLine("caution! : " + monstr); break;
					}

					var dt = new DateTime(int.Parse(yarstr), mon, int.Parse(daystr));
					dict[dt] = key;
				}

			}
			return dict;

		}

		private static Dictionary<string, long[]> GetCoinInfo(string uri) {

			var result = new Dictionary<string, long[]>();

			Task<string> webTask = GetWebPageAsync(new Uri(uri));
			webTask.Wait();
			string webSrc = webTask.Result;  // 結果を取得

			var parser = new HtmlParser();
			var document = parser.Parse(webSrc);
			var table = document.GetElementById("historical-data");
			foreach (var tr in table.GetElementsByTagName("tr")) {
				
				if(tr.ChildNodes.Length <= 13) {
					Debug.WriteLine("caution !! -- " + uri);
					continue;
				}

				var datestr = tr.ChildNodes[1].TextContent;
				var mcapstr = tr.ChildNodes[13].TextContent;
				var volmstr = tr.ChildNodes[11].TextContent;

				long mcap = 0;
				long.TryParse(mcapstr.Replace(",", ""), out mcap);
				long volm = 0;
				long.TryParse(volmstr.Replace(",", ""), out volm);

				result[datestr] = new long[] { mcap, volm };
			}
			return result;
		}

		private static List<string> GetCoinNameList() {

			string baseUri = "https://coinmarketcap.com/all/views/all/";

			Task<string> webTask = GetWebPageAsync(new Uri(baseUri));
			webTask.Wait(); // Mainメソッドではawaitできないので、処理が完了するまで待機する
			string webSrc = webTask.Result;  // 結果を取得

			var parser = new HtmlParser();
			var document = parser.Parse(webSrc);
			var elements = document.GetElementsByTagName("a");

			// 正規表現　https://msdn.microsoft.com/ja-jp/library/az24scfc(v=vs.110).aspx
			var regex = new Regex("^/currencies/[^/]+/#markets$");
			var coinnamelist = elements
				.Where(elm => elm.HasAttribute("href"))
				.Select(elm => elm.GetAttribute("href"))
				.Where(str => !string.IsNullOrEmpty(str) && regex.IsMatch(str))
				.Distinct()
				.Select(str => str.Substring("/currencies/".Length))
				.Select(str => str.Substring(0, str.Length - "/#markets".Length))
				.ToList()
				;

			return coinnamelist;

		}

		private static async Task<string> GetWebPageAsync(Uri uri) {
			using (HttpClient client = new HttpClient()) {
				client.Timeout = TimeSpan.FromSeconds(10.0);
				try {
					return await client.GetStringAsync(uri);
				}
				catch (Exception e) {
					Debug.WriteLine("例外メッセージ: {0} ", e.Message);
				}
				return null;
			}
		}




		//------------------//
		//-- Mainメソッド --//
		//------------------//

		static void Main(string[] args) {

			//-----------------//
			//-- Webクロール --//
			//-----------------//

			// コイン名を取得
			var coinlist = GetCoinNameList();
			coinlist = coinlist.Take(coincount > coinlist.Count ? coinlist.Count : coincount).ToList();

			// 各コインの情報を取得
			var resultlist = new Dictionary<string, Dictionary<string, long[]>>();
			foreach(string ext in coinlist) {
				string uri = ToCoinHistoricalDataUri(ext);
				resultlist[ext] = GetCoinInfo(uri);
				Thread.Sleep(interval);
			}

			// 日付情報の整理（ヘボい）
			var dateDict  = ToDateDict(resultlist.Values.Select(val => val.Keys).ToArray());
			var dates = dateDict.Keys.ToList();
			dates.Sort();


			//----------//
			//-- 出力 --//
			//----------//
			using (StreamWriter writer = new StreamWriter(outFileName)) {

				// ヘッダ行
				string header = "日付";
				if (outputMarkCp) { header += sep + string.Join(sep, coinlist.Select(coin => coin + "(m.c)")); }
				if (outputVolume) { header += sep + string.Join(sep, coinlist.Select(coin => coin + "(vol)")); }
				writer.WriteLine(header);

				// コンテンツ
				foreach (DateTime dt in dates) {
					var content = dt.ToLongDateString();
					if (outputMarkCp) {
						content += sep + string.Join("\t",
							coinlist
							.Select( coin =>
								(resultlist[coin].ContainsKey(dateDict[dt]))
								? resultlist[coin][dateDict[dt]][0]
								: 0
							)
							.Select(val => string.Format("{0,12}", val))
						);
					}
					if (outputVolume) {
						content += sep + string.Join("\t",
							coinlist
							.Select( coin =>
								(resultlist[coin].ContainsKey(dateDict[dt]))
								? resultlist[coin][dateDict[dt]][1]
								: 0
							)
							.Select( val => string.Format("{0,12}", val))
						);
					}
					writer.WriteLine(content);
				}
			}
		}


		
	}

}
