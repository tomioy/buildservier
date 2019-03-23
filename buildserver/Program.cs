using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;
using System.Net.WebSockets;

using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace buildserver
{
    class Program
    {
        
        static void Main(string[] args)
        {
            StartServer(args);
            Console.WriteLine("{0}:Server start.\nPress any key to exit.", DateTime.Now.ToString());
            Console.ReadKey();
            Parallel.ForEach(_client, p =>
            {
                if (p.State == WebSocketState.Open) p.CloseAsync(WebSocketCloseStatus.NormalClosure, "", System.Threading.CancellationToken.None);
            });
        }

        /// <summary>
        /// クライアントのWebSocketインスタンスを格納
        /// </summary>
        static List<WebSocket> _client = new List<WebSocket>();

        /// <summary>
        /// WebSocketサーバースタート
        /// </summary>
        static async void StartServer(string[] args)
        {
            string strHttp = string.Format("http://{0}/", args[0]);

            HttpListener httpListener;

            /// httpListenerで待ち受け
            httpListener = new HttpListener();
            httpListener.Prefixes.Add(strHttp); // "http://192.168.3.24:12345/"
            httpListener.Start();

            Console.WriteLine(strHttp);
            Console.WriteLine("/connect " + args[0]);

            while (true)
            {
                System.Diagnostics.Debug.WriteLine("GetContextAsync in");
                /// 接続待機
                HttpListenerContext listenerContext = await httpListener.GetContextAsync();
                System.Diagnostics.Debug.WriteLine("GetContextAsync out");


                if (listenerContext.Request.IsWebSocketRequest)
                {
                    System.Diagnostics.Debug.WriteLine("ProcessRequest");
                    /// httpのハンドシェイクがWebSocketならWebSocket接続開始
                    ProcessRequest(listenerContext);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("400&Close");
                    /// httpレスポンスを返す
                    listenerContext.Response.StatusCode = 400;
                    listenerContext.Response.Close();
                }
            }
        }

        /// <summary>
        /// WebSocket接続毎の処理
        /// </summary>
        /// <param name="listenerContext"></param>
        static async void ProcessRequest(HttpListenerContext listenerContext)
        {
            Console.WriteLine("{0}:New Session:{1}", DateTime.Now.ToString(), listenerContext.Request.RemoteEndPoint.Address.ToString());

            // WebSocketの接続完了を待機してWebSocketオブジェクトを取得する
            WebSocket ws = (await listenerContext.AcceptWebSocketAsync(subProtocol: null)).WebSocket;

            // 新規クライアントを追加
            _client.Add(ws);

            // ユーザー発言時のイベントをsubscribe
            string json = subscribePlayerChatEventCommand();
            Parallel.ForEach(_client,
                p => p.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json).ToArray()),
                WebSocketMessageType.Text,
                true,
                System.Threading.CancellationToken.None));

            // WebSocketの送受信ループ
            while (ws.State == WebSocketState.Open)
            {
                ArraySegment<byte> buff = new ArraySegment<byte>(new byte[4096]);

                // 受信待機
                WebSocketReceiveResult ret = await ws.ReceiveAsync(buff, System.Threading.CancellationToken.None);
                if (ret.MessageType == WebSocketMessageType.Text)
                {

                    string strbuff = Encoding.UTF8.GetString(buff.Take(ret.Count).ToArray());

                    while (ret.EndOfMessage != true)
                    {
                        ret = await ws.ReceiveAsync(buff, System.Threading.CancellationToken.None);
                        strbuff += Encoding.UTF8.GetString(buff.Take(ret.Count).ToArray());
                    }

                    Console.WriteLine("{0}:String Received:{1}", DateTime.Now.ToString(), listenerContext.Request.RemoteEndPoint.Address.ToString());
                    Console.WriteLine("Message={0}", strbuff);
                        
                        
                      
                    JObject jo = JObject.Parse(strbuff);
                    string strPurpose = jo["header"]["messagePurpose"].ToString();

                    // ユーザーが「build」と発言した場合だけ実行
                        if (strPurpose == "event")
                        {
                        string strEventname = jo["body"]["eventName"].ToString();
                        string strMessage = jo["body"]["properties"]["Message"].ToString();
                        if (strEventname == "PlayerMessage")
                        {
                            switch (strMessage)
                            {
                                case "build1":
                                    // 石ブロックを配置するリクエストを送信
                                    Console.WriteLine(">>> start build");
                                    setBlockCommand("~0", "~0", "~0", "stonebrick");
                                    break;
                                case "build12":
                                    // 石ブロックを配置するリクエストを送信
                                    Console.WriteLine(">>> start build2");
                                    setBlockCommand("~0", "~0", "~0", "acacia_door");
                                    break;
                                case "fill10x10":
                                    Console.WriteLine(">>> start fill");
                                    fillCommand("~1", "~0", "~1", "~10", "~0", "~10", "stained_glass");
                                    break;

                                case "house":
                                    Console.WriteLine(">>> start build house");
                                    buildhouse(7,3,7);
                                    break;

                                default:
                                    Console.WriteLine(">>> not Mesage");
                                    break;
                                        
                            }
                        }
                    }

                }
                else if (ret.MessageType == WebSocketMessageType.Close) /// クローズ
                {
                    Console.WriteLine("{0}:Session Close:{1}", DateTime.Now.ToString(), listenerContext.Request.RemoteEndPoint.Address.ToString());
                    break;
                }
            }

            /// クライアントを除外する
            _client.Remove(ws);
            ws.Dispose();

        }

        // ユーザー発言時のイベント登録用JSON文字列を生成する関数
        //  https://gist.github.com/jocopa3/5f718f4198f1ea91a37e3a9da468675c
        static string subscribePlayerChatEventCommand()
        {
            string json;

            json = "{\"body\": {\"eventName\": \"PlayerMessage\"},\"header\": {\"requestId\": \"xxxxxxxx-xxxx-xxxx-xxxxxxxxxxxxxxxxx\",\"messagePurpose\": \"subscribe\",\"version\": 1,\"messageType\": \"commandRequest\"}}";

            return json;
        }

        // 石ブロックを配置するリクエスト用JOSN文字列を生成する関数
        //  https://gist.github.com/jocopa3/54b42fb6361952997c4a6e38945e306f
        static void setBlockCommand(string x, string y, string z, string blockType)
        {
            string strLine = string.Format("setblock {0} {1} {2} {3}",x, y, z, blockType);
            string strJson = makeCommand(strLine);
            sendCommand(strJson);
        }
        static void fillCommand(string x1, string y1, string z1, string x2, string y2, string z2, string blockType)
        {
            string strLine = string.Format("fill {0} {1} {2} {3} {4} {5} {6}", x1, y1, z1, x2, y2, z2, blockType);
            string strJson = makeCommand(strLine);
            sendCommand(strJson);
        }
        // ComandRequest JOSN 文字列生成
        static string makeCommand(string strLine)
        {
            string json;

            json = "{ \"body\": { \"origin\": {\"type\": \"player\"},\"commandLine\": \"";
            json += string.Format(" {0} ", strLine);
            json += "\",\"version\": 1},\"header\": {\"requestId\": \"xxxxxxxx-xxxx-xxxx-xxxxxxxxxxxxxxxxx\",\"messagePurpose\": \"commandRequest\",\"version\": 1,\"messageType\": \"commandRequest\"}}";

            return json;
        }

        static void sendCommand(string strJson)
        {
            Console.WriteLine(">SendMessage={0}", strJson);

            /// 各クライアントへ配信
            Parallel.ForEach(_client,
                p => p.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(strJson).ToArray()),
                WebSocketMessageType.Text,
                true,
                System.Threading.CancellationToken.None));
        }

        /// <summary>
        /// buildhouse 家を建てる
        /// </summary>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <param name="d"></param>
        static void buildhouse(int w, int h, int d)
        {
            floor(w, h, d);
            wall(w, h, d);
            roof(w, h, d);
            roofwall(w, h, d);
            window(w, h, d);
            door(w, h, d);
        }
        static void floor(int w, int h, int d)
        {
            fillCommand("~1", "~-1", "~1", string.Format("~{0}", w), "~-1", string.Format("~{0}", d), "planks");

        }
        static void wall(int w, int h, int d)
        {
            fillCommand("~1", "~0", "~1", "~1", string.Format("~{0}", h), string.Format("~{0}", d), "planks");
            fillCommand("~1", "~0", "~1", string.Format("~{0}", w), string.Format("~{0}", h),"~1", "planks");
            fillCommand(string.Format("~{0}", w), "~0", string.Format("~{0}", d), "~1", string.Format("~{0}", h), string.Format("~{0}", d), "planks");
            fillCommand(string.Format("~{0}", w), "~0", string.Format("~{0}", d), string.Format("~{0}", w), string.Format("~{0}", h), "~1", "planks");
        }
        static void roof(int w, int h, int d)
        {
            int ix = -1;
            int iHight = h;
            int iStep = w / 2 + 1;

            for (int conter = 0; conter <= iStep; conter++)
            {
                iHight++;
                ix++;

                fillCommand(string.Format("~{0}", ix), string.Format("~{0}", iHight), "~0", string.Format("~{0}", ix), string.Format("~{0}", iHight), string.Format("~{0}", d+1), "stained_glass");
            }

            iHight = h;
            ix = w+2;
            for (int conter = 0; conter <= iStep; conter++)
            {
                iHight++;
                ix--;

                fillCommand(string.Format("~{0}", ix), string.Format("~{0}", iHight), "~0", string.Format("~{0}", ix), string.Format("~{0}", iHight), string.Format("~{0}", d + 1), "stained_glass");
            }

        }
        static void roofwall(int w, int h, int d)
        {
            int ix = 0;
            int iHight = h;
            int iStep = w / 2 + 1;

            for (int conter = 0; conter < iStep; conter++)
            {
                iHight++;
                ix++;

                fillCommand(string.Format("~{0}", ix), string.Format("~{0}", iHight),  "~1", string.Format("~{0}", w-ix+1 ), string.Format("~{0}", iHight), "~1", "planks");
            }

            iHight = h;
            ix = w + 1;
            for (int conter = 0; conter < iStep; conter++)
            {
                iHight++;
                ix--;

                fillCommand(string.Format("~{0}", ix), string.Format("~{0}", iHight), string.Format("~{0}", d), string.Format("~{0}", w - ix + 1), string.Format("~{0}", iHight), string.Format("~{0}", d), "planks");
            }
        }
        static void window(int w, int h, int d)
        {
            int ix = w / 2;
            int iz = d / 2;

            fillCommand(string.Format("~{0}", ix), "~1", string.Format("~{0}", d), string.Format("~{0}", ix+1), "~2", string.Format("~{0}", d), "glass_pane");
            fillCommand(string.Format("~{0}", w), "~1", string.Format("~{0}", iz), string.Format("~{0}", w), "~2", string.Format("~{0}", iz+1), "glass_pane");
            fillCommand("~1", "~1", string.Format("~{0}", iz), "~1", "~2", string.Format("~{0}", iz+1), "glass_pane");
        }
        static void door(int w, int h, int d)
        {
            int ix = w / 2 + 1;
            setBlockCommand(string.Format("~{0}", ix), "~0", "~1", "acacia_door");

        }
    }
}
