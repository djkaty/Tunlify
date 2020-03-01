/* Tunlify
 * (c) Katy Coe 2020 - https://github.com/djkaty - http://www.djkaty.com
 */

using System;
using System.Net;
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
    }

    class Program
    {
        public static void Main(string[] args) {
            Parser.Default.ParseArguments<Options>(args).MapResult(
                options => Run(options),
                _ => 1);
        }

        private static int Run(Options options) {
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
            return 0;
        }
    }
}
