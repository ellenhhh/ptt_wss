using System;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;  
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;


namespace Websocket.Client.Sample
{
    class Program
    {
        private static readonly ManualResetEvent ExitEvent = new ManualResetEvent(false);               

        static string pttUrl = "https://term.ptt.cc";
        static string pttWssUrl = "wss://ws.ptt.cc/bbs";
        static string pttUser = ""; // ellen: your ptt account
        static string pttPassword = ""; // ellen: your ptt password
        static bool isUserLogIn = false;

        static void Main(string[] args)
        {
            // ellen: Register big5 because .NET Core doesn't support big5
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding big5 = Encoding.GetEncoding(950);

            InitLogging();

            AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
            AssemblyLoadContext.Default.Unloading += DefaultOnUnloading;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            Console.WriteLine("|=======================|");
            Console.WriteLine("|    WEBSOCKET CLIENT   |");
            Console.WriteLine("|=======================|");
            Console.WriteLine();

            Log.Debug("====================================");
            Log.Debug("              STARTING              ");
            Log.Debug("====================================");

            var factory = new Func<ClientWebSocket>(() =>
            {
                var client = new ClientWebSocket
                {
                    Options =
                    {
                        KeepAliveInterval = TimeSpan.FromSeconds(5),
                        // Proxy = ...
                        // ClientCertificates = ...
                    }
                };
                client.Options.SetRequestHeader("Origin", pttUrl);
                return client;
            });

            var url = new Uri(pttWssUrl);

            using (IWebsocketClient client = new WebsocketClient(url, factory))
            {
                client.Name = "Ptt";
                client.ReconnectTimeout = TimeSpan.FromSeconds(30);
                client.ErrorReconnectTimeout = TimeSpan.FromSeconds(30);
                client.ReconnectionHappened.Subscribe(type =>
                {
                    Log.Information($"Reconnection happened, type: {type}, url: {client.Url}");
                });
                client.DisconnectionHappened.Subscribe(info =>
                    Log.Warning($"Disconnection happened, type: {info.Type}"));

                client.MessageReceived.Subscribe(msg =>
                {
                    // ellen: assume the encoding is big5. Not sure if there will be other encoding 
                    string returnedMsg = big5.GetString(msg.Binary);     

                    // ellen: temp solution to check if user log in successfully
                    if(returnedMsg.Contains("請按任意鍵繼續"))
                    {
                        isUserLogIn = true;                        
                    }                        

                    Console.WriteLine(returnedMsg);
                    Log.Information($"Message received: {returnedMsg}");
                });

                Log.Information("Starting...");
                client.Start().Wait();
                Log.Information("Started.");

                // ellen: Assume user can log in successfully
                // Need to handle 1. fail auth, 2. too many guests on ptt, 3. user already login
                if(!isUserLogIn)
                {                                    
                    byte[] userInBytes = Encoding.Unicode.GetBytes(pttUser + Environment.NewLine);
                    byte[] pwInBytes = Encoding.Unicode.GetBytes(pttPassword + Environment.NewLine);
                    byte[] newLineInBytes = Encoding.Unicode.GetBytes(Environment.NewLine);

                    client.Send(userInBytes);
                    client.Send(pwInBytes);
               
                    // ellen: Send newline to handle Press any key here
                    client.Send(newLineInBytes);
                }
                
                //Task.Run(() => StartSendingPing(client));
                //Task.Run(() => SwitchUrl(client));

                ExitEvent.WaitOne();
            }

            Log.Debug("====================================");
            Log.Debug("              STOPPING              ");
            Log.Debug("====================================");
            Log.CloseAndFlush();
        }

        /* private static async Task StartSendingPing(IWebsocketClient client)
        {
            while (true)
            {
                await Task.Delay(1000);

                if(!client.IsRunning)
                    continue;

                client.Send("ping");
            }
        } 
        
        private static async Task SwitchUrl(IWebsocketClient client)
        {
            while (true)
            {
                await Task.Delay(20000);
                
                var production = new Uri("wss://www.bitmex.com/realtime");
                var testnet = new Uri("wss://testnet.bitmex.com/realtime");

                var selected = client.Url == production ? testnet : production;
                client.Url = selected;
                await client.Reconnect();
            }
        }
 */
        private static void InitLogging()
        {
            var executingDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
            var logPath = Path.Combine(executingDir, "logs", "verbose.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                //.WriteTo.ColoredConsole(LogEventLevel.Verbose, 
                //    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message} {NewLine}{Exception}")
                .CreateLogger();
        }

        private static void CurrentDomainOnProcessExit(object sender, EventArgs eventArgs)
        {
            Log.Warning("Exiting process");
            ExitEvent.Set();
        }

        private static void DefaultOnUnloading(AssemblyLoadContext assemblyLoadContext)
        {
            Log.Warning("Unloading process");
            ExitEvent.Set();
        }

        private static void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Log.Warning("Canceling process");
            e.Cancel = true;
            ExitEvent.Set();
        }
    }
}