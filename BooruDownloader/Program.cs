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

                // Define command options and arguments
                var outputPathArgument = command.Argument("path", "Output path.", false);
                var startIdOption = command.Option<long>("-s|--start-id <id>", "Starting Id. Default is 1.", CommandOptionType.SingleValue);
                var endIdOption = command.Option<long>("-e|--end-id <id>", "Ending Id. Default is 0 (unlimited).", CommandOptionType.SingleValue);
                var ignoreHashCheckOption = command.Option("-i|--ignore-hash-check", "Ignore hash check.", CommandOptionType.NoValue);
                var includeDeletedOption = command.Option("-d|--deleted", "Include deleted posts.", CommandOptionType.NoValue);
                var parallelDownloadsOption = command.Option<int>("-p|--parallel-downloads <value>", "Number of images to download simultaneously. Default is 5.", CommandOptionType.SingleValue);

                // Add booruSiteOption
                var booruSiteOption = command.Option("-b|--boorusite <string>", "Specify the booru site. Default is https://safebooru.org/.", CommandOptionType.SingleValue);

                command.OnExecute(() =>
                {
                    long startId = startIdOption.HasValue() ? startIdOption.ParsedValue : 1;
                    long endId = endIdOption.HasValue() ? endIdOption.ParsedValue : 0;
                    int parallelDownloads = parallelDownloadsOption.HasValue() ? parallelDownloadsOption.ParsedValue : 5;
                    bool ignoreHashCheck = ignoreHashCheckOption.HasValue();
                    bool includeDeleted = includeDeletedOption.HasValue();

                    // Parse booru site option
                    string booruSite = ParseBooruSiteOption(booruSiteOption.Value);

                    DumpCommand.Run(outputPathArgument.Value, startId, endId, parallelDownloads, ignoreHashCheck, includeDeleted, booruSite).Wait();

                    return 0;
                });

                // Modify help text to include available booru sites
                command.HelpOption("-help");
                command.ExtendedHelpText = "\nAvailable booru sites:\n" +
                                            "1) (SFW)  https://safebooru.org/   \n" +
                                            "2) (NSFW) https://rule34.xxx/      \n" +
                                            "3) (NSFW) https://realbooru.com/   \n" +
                                            "4) (NSFW) https://konachan.com/    \n" +
                                            "5) (SFW)  https://konachan.net/    \n" +
                                            "6) (NSFW) https://xbooru.com/      \n" +
                                            "7) (NSFW) https://hypnohub.net/    \n";
            });

            application.OnExecute(() =>
            {
                application.ShowHint();
                return 0;
            });

            ExecuteApplication(application, args);
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
