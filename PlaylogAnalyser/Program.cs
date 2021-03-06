﻿using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Builder;
using System.IO;
using System.Linq;

namespace PlaylogAnalyser
{
    enum SortOrder
    {
        Name,
        Duration,
    }

    class Program
    {
        static int Main(string[] args)
        {
#if DEBUG
            if (args.Length == 0)
            {
                Main(new[]
                {
                    "--path", @"E:\Notes\mpvlogs",
                    "--start", DateTime.UnixEpoch.ToString(),
                    "--end", DateTime.Now.ToString(),
                    "--order", SortOrder.Duration.ToString()
                });
                Main(new[]
                {
                    "compare",
                    "--path", @"E:\Notes\mpvlogs",
                });
                Console.ReadLine();
                return 0;
            }
#endif
            var rootCommand = new RootCommand
            {
                new Option<string>(
                    "--path",
                    getDefaultValue: () => Directory.GetCurrentDirectory(),
                    description: "Log directory"),
                new Option<DateTime>(
                    new []{"--start", "-s" },
                    () => DateTime.UnixEpoch.ToLocalTime(),
                    "Start date filter"),
                new Option<DateTime>(
                    new []{ "--end", "-e" },
                    () => DateTime.Now.ToLocalTime(),
                    "End date filter"),
                new Option<SortOrder>(
                    "--order",
                    () => SortOrder.Duration,
                    "Sort ordering")
            };


            rootCommand.Description = "MPV Playlog analyser";

            // Note that the parameters of the handler method are matched according to the names of the options
            rootCommand.Handler = CommandHandler.Create<string, DateTime, DateTime, SortOrder>(Analyse);

            var compareCommand = new Command("compare", "Retrieve the logs from file names in standard input");

            var sourceOpt = new Option<string>(
                "--path",
                getDefaultValue: () => Directory.GetCurrentDirectory(),
                description: "Log directory"
            );
            compareCommand.AddOption(sourceOpt);

            compareCommand.Handler = CommandHandler.Create<string>(Compare);

            rootCommand.AddCommand(compareCommand);

            // Parse the incoming args and invoke the handler
            return rootCommand.InvokeAsync(args).Result;
        }

        static void Compare(string path)
        {
            Console.WriteLine($"Comparing {path}");
            
            var data = GetAnalysables(path, DateTime.UnixEpoch, DateTime.Now).ToDictionary(x => x.file, x => x.sum);

            string line;
            while ((line = Console.ReadLine()) != null)
            {
                try
                {
                    var target = Path.GetFileNameWithoutExtension(line);
                    var entry = data[target];
                    Console.WriteLine($"{target,-110} {entry,6:N0} s");
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"{e.GetType().Name}: {e.Message}");
                }
            }
        }

        static void Analyse(string path, DateTime start, DateTime end, SortOrder order)
        {
            var summed = GetAnalysables(path, start, end);
            var ranks = GetRank(summed, order);

            foreach (var (file, sum) in ranks)
            {
                Console.WriteLine($"{file,-110} {sum,6:N0} s");
            }
        }

        static IEnumerable<(string file, double sum)> GetAnalysables(string path, DateTime start, DateTime end)
        {
            start = start.ToLocalTime();
            end = end.ToLocalTime();
            Console.WriteLine($"{start} - {end}");
            var files = Directory.EnumerateFiles(path, "*.dat");
            var valids = ParseFiles(files);
            var dateFiltered = valids.AsParallel().Select(x => (x.file, timestamps: x.timestamps.Where(y => y.start >= start && y.end <= end)));
            var summed = dateFiltered.Select(x => (x.file, sum: x.timestamps.Sum(y => (y.end - y.start).TotalSeconds))).Where(x => x.sum > 0);
            return summed;
        }

        static IEnumerable<(string file, double sum)> GetRank(IEnumerable<(string file, double sum)> summed, SortOrder sortOrder) => sortOrder switch
        {
            SortOrder.Name => summed.OrderBy(x => x.file),
            SortOrder.Duration => summed.OrderBy(x => x.sum),
            _ => throw new NotImplementedException(),
        };


        static IEnumerable<(string file, IEnumerable<(DateTime start, DateTime end)> timestamps)> ParseFiles(IEnumerable<string> files)
        {
            foreach (var item in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(item);
                yield return (fileName, ParseFile(item, fileName));
            }
        }

        static IEnumerable<(DateTime start, DateTime end)> ParseFile(string filePath, string fileName)
        {
            using var reader = new StreamReader(filePath);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                (DateTime start, DateTime end) output;
                try
                {
                    output = ParseLine(line);
                }
                catch (InvalidDataException)
                {
                    continue;
                }
                catch (Exception e)
                {
                    var origin = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"{fileName} {e.Message}");
                    Console.ForegroundColor = origin;
                    yield break;
                }
                yield return output;
            }
        }

        static (DateTime start, DateTime end) ParseLine(string line)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrWhiteSpace(line))
            {
                throw new InvalidDataException("This is allowable");
            }
            var splits = line.Split(" ");

            if (splits.Length != 2)
            {
                throw new FormatException("Parse failed: Line split does not yield 2 parts.");
            }

            var start = double.Parse(splits[0]);
            var end = double.Parse(splits[1]);

            if (start > end)
            {
                throw new Exception("Parse failed: Bad time records.");
            }

            return (UnixTimeStampToDateTime(start), UnixTimeStampToDateTime(end));
        }

        public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
        {
            return DateTime.UnixEpoch.AddMilliseconds(unixTimeStamp).ToLocalTime();
        }
    }
}
