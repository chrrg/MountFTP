using System;
using Forge.MountFTP;

namespace Forge.MountFTP.CommandLine
{
    class Program
    {
        static object writeToken = new { };

        static void Main(string[] args)
        {
            //var conf = Configuration.Configure<Options>().CreateAndBind(args);
            var conf = new Options
            {
                HostName = "172.16.0.63",
                UserName="anonymous",
                Password="",
                DriveLetter='X',
            };
            var drive = new Drive(conf);
            drive.FtpCommand += new LogEventHandler(OnFtpCommand);
            drive.FtpServerReply += new LogEventHandler(OnFtpServerReply);
            drive.FtpClientMethodCall += new LogEventHandler(OnFtpClientMethodCall);
            drive.FtpClientDebug += new LogEventHandler(OnFtpClientDebug);

            Console.WriteLine(drive.Mount());
            Console.ReadKey();
        }

        static void WriteColoredLine(string message, ConsoleColor foreGroundColor)
        {
            lock (writeToken)
            {
                Console.ForegroundColor = foreGroundColor;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }

        static void OnFtpCommand(object sender, LogEventArgs args)
        {
            WriteColoredLine(args.Message, ConsoleColor.Red);
        }

        static void OnFtpServerReply(object sender, LogEventArgs args)
        {
            WriteColoredLine(args.Message, ConsoleColor.DarkRed);
        }

        static void OnFtpClientMethodCall(object sender, LogEventArgs args)
        {
            WriteColoredLine(args.Message, ConsoleColor.Yellow);
        }

        static void OnFtpClientDebug(object sender, LogEventArgs args)
        {
            WriteColoredLine(args.Message, ConsoleColor.DarkYellow);
        }
    }
}