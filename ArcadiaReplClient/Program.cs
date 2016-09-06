using System;
using System.Text;
using System.IO;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using clojure.lang;

namespace ArcadiaReplClient
{
	class MainClass
	{
		static Thread socketThread;
		static Thread cliThread;
		static bool running = true;

		public static bool OnWindows()
		{
			return Environment.OSVersion.Platform != PlatformID.MacOSX &&
				   Environment.OSVersion.Platform != PlatformID.Unix;
		}

		public static bool OldCLR()
		{
			return Environment.Version.Major <= 2;
		}

		public static void UdpSend(UdpClient s, object str)
		{
			var bs = Encoding.UTF8.GetBytes(str.ToString());
			s.Send(bs, bs.Length);
		}

		public static string ReadLine()
		{
			// built in implementation is fine on these platforms, use it
			if (OnWindows() || !OldCLR())
				return Console.ReadLine();

			// old mono's Console.ReadLine does not display first character sometimes
			// so here's our own Console.ReadLine -nasser
			var sb = new StringBuilder("");
			var key = Console.ReadKey();
			while (key.Key != ConsoleKey.Enter)
			{
				if (key.Key == ConsoleKey.Backspace)
				{
					if (sb.Length > 0)
					{
						sb.Remove(sb.Length - 1, 1);
					}
					else
					{
						Console.Write(" ");
					}
				}
				else
				{
					sb.Append(key.KeyChar);
				}

				key = Console.ReadKey();
			}
			return sb.ToString();
		}

		public static void Main(string[] args)
		{
			// listens for standard input
			// checks validity
			// sends code to unity for evaluation
			cliThread = new Thread((object socket) =>
			{
				var s = (UdpClient)socket;
				var sb = new StringBuilder("");
				while (running)
				{
					var cliIn = ReadLine() + "\n";
					sb.Append(cliIn);
					try
					{
						// attempt read just to throw exception on invalid form
						LispReader.read(new PushbackTextReader(new StringReader(sb.ToString())), true, null, true);
						UdpSend(s, sb);
						sb.Length = 0;// sb.Clear(); // no StringBuilder.Clear() in .NET 3.5
					}
					catch (EndOfStreamException)
					{
						// keep looping in EOF
						// hang on to string builder to build up balanced form
						continue;
					}
					catch (Exception e)
					{
						// clear string builder on all other errors
						sb.Length = 0;// sb.Clear(); // no StringBuilder.Clear() in .NET 3.5
						Console.WriteLine(e);
					}
				}
			});

			// listening for responses from unity
			// writes to standard out
			socketThread = new Thread((object sock) =>
			{
				var socket = (UdpClient)sock;
				IPEndPoint ip = new IPEndPoint(0, 0);
				while (running)
				{
					try
					{
						// incoming bytes include namespace qualified prompt
						var bytes = socket.Receive(ref ip);
						var instr = Encoding.UTF8.GetString(bytes);
						Console.Write(instr);
					}
					catch (SocketException)
					{
						// keep looping on recieve timeout
						continue;
					}
				}
			});

			Console.CancelKeyPress += (sender, e) =>
			{
				// graceful exit
				Console.WriteLine("\n");
				running = false;
			};

			if (Environment.GetEnvironmentVariable("CLOJURE_LOAD_PATH") == null)
				Environment.SetEnvironmentVariable("CLOJURE_LOAD_PATH", Path.Combine("..", "Source"));

			var unitySocket = new UdpClient();
			unitySocket.Connect("localhost", 11211);
			unitySocket.Client.ReceiveTimeout = 500;
			socketThread.Start(unitySocket);
			cliThread.Start(unitySocket);
			UdpSend(unitySocket,
					@"(binding [*warn-on-reflection* false]
			        	(do
							(println ""; Arcadia REPL"")
							(println (str ""; Clojure "" (clojure-version)))
							(println (str ""; Unity "" (UnityEditorInternal.InternalEditorUtility/GetFullUnityVersion)))
							(println (str ""; Mono "" (.Invoke (.GetMethod Mono.Runtime ""GetDisplayName"" (enum-or BindingFlags/NonPublic BindingFlags/Static)) nil nil)))))");

			// force clojure load on main thread
			RT.nextID();
		}
	}
}
