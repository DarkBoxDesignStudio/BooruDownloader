using BooruDownloader.Commands;
using BooruDownloader.Utilities;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.PlatformAbstractions;
using System;

namespace BooruDownloader
{
    class Program
    {
        static void Main(string[] args)
        {
            var application = new CommandLineApplication(throwOnUnexpectedArg: true) { FullName = "Booru Downloader" };
            application.HelpOption("-?|-h|--help");
            application.VersionOption("-v|--version", PlatformServices.Default.Application.ApplicationVersion);

            application.Command("dump", command =>
            {
                command.Description = "Download entire images on the server of specified source.";
                command.HelpOption("-h|--help");

                command.ExtendedHelpText = "\nAvailable booru sites:\n" +
                            "1) (SFW)  https://safebooru.org/   \n" +
                            "2) (NSFW) https://rule34.xxx/      \n" +
                            "3) (NSFW) https://realbooru.com/   \n" +
                            "4) (NSFW) https://konachan.com/    \n" +
                            "5) (SFW)  https://konachan.net/    \n" +
                            "6) (NSFW) https://xbooru.com/      \n" +
                            "7) (NSFW) https://hypnohub.net/    \n";

                // Define command options and arguments
                var outputPathArgument = command.Argument("path", "Output path.", false);
                var startIdOption = command.Option("-s|--start-id <id>", "Starting Id. Default is 1.", CommandOptionType.SingleValue);
                var endIdOption = command.Option("-e|--end-id <id>", "Ending Id. Default is 0 (unlimited).", CommandOptionType.SingleValue);
                var ignoreHashCheckOption = command.Option("-i|--ignore-hash-check", "Ignore hash check.", CommandOptionType.NoValue);
                var includeDeletedOption = command.Option("-d|--deleted", "Include deleted posts.", CommandOptionType.NoValue);
                var parallelDownloadsOption = command.Option("-p|--parallel-downloads <value>", "Number of images to download simultaneously. Default is 5.", CommandOptionType.SingleValue);

                // Add booruSiteOption
                var booruSiteOption = command.Option("-b|--boorusite <string> || <int>", "Specify the booru site. Default is https://safebooru.org/.", CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    string path = outputPathArgument.Value;
                    long startId = 1;
                    long endId = 0;
                    int parallelDownloads = 5;
                    bool ignoreHashCheck = ignoreHashCheckOption.HasValue();
                    bool includeDeleted = includeDeletedOption.HasValue();

                    string booruSite = ParseBooruSiteOption(booruSiteOption);

                    if (startIdOption.HasValue() && !long.TryParse(startIdOption.Value(), out startId))
                    {
                        Console.WriteLine("Invalid start id.");
                        return -2;
                    }

                    if (endIdOption.HasValue() && !long.TryParse(endIdOption.Value(), out endId))
                    {
                        Console.WriteLine("Invalid end id.");
                        return -2;
                    }
                    if (parallelDownloadsOption.HasValue() && !int.TryParse(parallelDownloadsOption.Value(), out parallelDownloads))
                    {
                        Console.WriteLine("Invalid number of parallel downloads.");
                        return -2;
                    }

                    DumpCommand.Run(outputPathArgument.Value, startId, endId, parallelDownloads, ignoreHashCheck, includeDeleted, booruSite).Wait();

                    return 0;
                });
            });

            application.OnExecute(() =>
            {
                application.ShowHint();
                return 0;
            });

            ExecuteApplication(application, args);
        }

        private static string ParseBooruSiteOption(CommandOption booruSiteOption)
        {
            if (booruSiteOption.HasValue())
            {
                string input = booruSiteOption.Value();

                if (int.TryParse(input, out int siteNumber))
                {
                    switch (siteNumber)
                    {
                        case 1:
                            return "https://safebooru.org";
                        case 2:
                            return "https://rule34.xxx";
                        case 3:
                            return "https://realbooru.com";
                        case 4:
                            return "https://konachan.com";
                        case 5:
                            return "https://konachan.net";
                        case 6:
                            return "https://xbooru.com";
                        case 7:
                            return "https://hypnohub.net";
                        default:
                            Console.WriteLine("Invalid booru site number.");
                            return null;
                    }
                }
                else
                {
                    string[] validUrls = {
                        "https://safebooru.org",
                        "https://rule34.xxx",
                        "https://realbooru.com",
                        "https://konachan.com",
                        "https://konachan.net",
                        "https://xbooru.com",
                        "https://hypnohub.net"
                };

                    if (Array.IndexOf(validUrls, input) != -1)
                    {
                        return input;
                    }
                    else
                    {
                        // Accept any URL, even if it's not in the list of valid URLs
                        return input;
                    }
                }
            }

            return null;
        }

        private static void ExecuteApplication(CommandLineApplication application, string[] args)
        {
            try
            {
                int exitCode = application.Execute(args);

                if (exitCode == -2) application.ShowHint();

                Environment.ExitCode = exitCode;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Environment.ExitCode = -1;
            }
        }
    }
}
