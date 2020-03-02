/* Tunlify
 * (c) Katy Coe 2020 - https://github.com/djkaty - http://www.djkaty.com
 */

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using CommandLine;
using Tunlify.Common;

namespace Tunlify.CLI
{
    public class Options
    {
        [Option("src", Required = true, HelpText = "Source IP address and port eg. 127.0.0.1:1234")]
        public string SourceIP { get; set; }

        [Option("dst", Required = true, HelpText = "Destination IP address and port eg. 127.0.0.1:1234")]
        public string DestIP { get; set; }

        [Option("log", Required = false, HelpText = "Pathname to traffic capture file")]
        public string LogFilePath { get; set; }
    }

    class Program
    {
        public static async Task<int> Main(string[] args) =>
            await Parser.Default.ParseArguments<Options>(args).MapResult(
                async options => await Run(options),
                async _ => await Task.FromResult(1));

        private static async Task<int> Run(Options options) {
            if (!IPEndPoint.TryParse(options.SourceIP, out var sourceIP)) {
                Console.Error.WriteLine("Invalid source IP specified");
                return 1;
            }

            if (!IPEndPoint.TryParse(options.DestIP, out var destIP)) {
                Console.Error.WriteLine("Invalid destination IP specified");
                return 1;
            }

            Console.WriteLine(sourceIP);
            Console.WriteLine(destIP);

            var tunnel = new Tunnel(sourceIP, destIP);

            // Capture data to log file if requested
            FileStream logFile = null;

            if (!string.IsNullOrEmpty(options.LogFilePath)) {
                logFile = new FileStream(options.LogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);

                tunnel.ReceiveFromClient += async (s, e) => await logFile.WriteAsync(e.Data);
                tunnel.ReceiveFromServer += async (s, e) => await logFile.WriteAsync(e.Data);
            }

            try {
                await tunnel.StartAsync();
            }
            catch (SocketException ex) {
                Console.Error.WriteLine(ex.Message + " (Windows Sockets error code " + ex.ErrorCode + ")");
            }
            finally {
                logFile?.Close();
            }

            return 0;
        }
    }
}
