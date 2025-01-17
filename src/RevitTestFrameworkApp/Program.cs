﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dynamo.Utilities;
using Microsoft.Practices.Prism;
using NDesk.Options;
using RevitTestFrameworkApp.Properties;

namespace RevitTestFrameworkApp
{
    class Program
    {
        private static ViewModel _vm;
        private static Runner.Runner runner;
        
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyHelper.CurrentDomain_AssemblyResolve;

            try
            {
                runner = new Runner.Runner();
                _vm = new ViewModel(runner);

                if (!ParseArguments(args))
                {
                    return;
                }

                var products = Runner.Runner.FindRevit();
                if (products == null)
                {
                    return;
                }

                runner.Products.AddRange(products);
                
                if (runner.Gui)
                {
                    LoadSettings();

                    if (!string.IsNullOrEmpty(runner.TestAssembly) && File.Exists(runner.TestAssembly))
                    {
                        runner.Refresh();
                    }

                    // Show the user interface
                    var view = new View(_vm);
                    view.ShowDialog();


                    SaveSettings();
                }
                else
                {
                    if (string.IsNullOrEmpty(runner.RevitPath))
                    {
                        runner.RevitPath = Path.Combine(runner.Products.First().InstallLocation, "revit.exe");
                    }

                    if (string.IsNullOrEmpty(runner.WorkingDirectory))
                    {
                        runner.WorkingDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    }

                    // In any case here, the test assembly cannot be null
                    if (string.IsNullOrEmpty(runner.TestAssembly))
                    {
                        Console.WriteLine("You must specify at least a test assembly.");
                        return;
                    }

                    var assemblyDatas = Runner.Runner.ReadAssembly(runner.TestAssembly, runner.WorkingDirectory);
                    if (assemblyDatas == null)
                    {
                        return;
                    }

                    runner.Assemblies.Clear();
                    runner.Assemblies.AddRange(assemblyDatas);

                    if (File.Exists(runner.Results) && !runner.Concat)
                    {
                        File.Delete(runner.Results);
                    }

                    Console.WriteLine(runner.ToString());

                    if (string.IsNullOrEmpty(runner.Fixture) && string.IsNullOrEmpty(runner.Test))
                    {
                        runner.RunCount = runner.Assemblies.SelectMany(a => a.Fixtures.SelectMany(f => f.Tests)).Count();
                        foreach (var ad in runner.Assemblies)
                        {
                            runner.RunAssembly(ad);
                        }
                    }
                    else if (string.IsNullOrEmpty(runner.Test) && !string.IsNullOrEmpty(runner.Fixture))
                    {
                        var fd = runner.Assemblies.SelectMany(x => x.Fixtures).FirstOrDefault(f => f.Name == runner.Fixture);
                        if (fd != null)
                        {
                            runner.RunCount = fd.Tests.Count;
                            runner.RunFixture(fd);
                        }
                    }
                    else if (string.IsNullOrEmpty(runner.Fixture) && !string.IsNullOrEmpty(runner.Test))
                    {
                        var td =
                            runner.Assemblies.SelectMany(a => a.Fixtures.SelectMany(f => f.Tests))
                                .FirstOrDefault(t => t.Name == runner.Test);
                        if (td != null)
                        {
                            runner.RunCount = 1;
                            runner.RunTest(td);
                        }
                    }
                }

                runner.Cleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void SaveSettings()
        {
            Settings.Default.workingDirectory = runner.WorkingDirectory;
            Settings.Default.assemblyPath = runner.TestAssembly;
            Settings.Default.resultsPath = runner.Results;
            Settings.Default.isDebug = runner.IsDebug;
            Settings.Default.timeout = runner.Timeout;
            Settings.Default.selectedProduct = runner.SelectedProduct;
            Settings.Default.Save();
        }

        private static void LoadSettings()
        {
            runner.WorkingDirectory = !String.IsNullOrEmpty(Settings.Default.workingDirectory)
                ? Settings.Default.workingDirectory
                : Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            runner.TestAssembly = !String.IsNullOrEmpty(Settings.Default.assemblyPath)
                ? Settings.Default.assemblyPath
                : null;

            runner.Results = !String.IsNullOrEmpty(Settings.Default.resultsPath)
                ? Settings.Default.resultsPath
                : null;

            runner.Timeout = Settings.Default.timeout;
            runner.IsDebug = Settings.Default.isDebug;

            if (Settings.Default.selectedProduct > runner.Products.Count - 1)
            {
                runner.SelectedProduct = -1;
            }
            else
            {
                runner.SelectedProduct = Settings.Default.selectedProduct;
            }
        }

        private static bool ParseArguments(IEnumerable<string> args)
        {
            var showHelp = false;

            var p = new OptionSet()
            {
                {"dir:","The path to the working directory.", v=> runner.WorkingDirectory = Path.GetFullPath(v)},
                {"a:|assembly:", "The path to the test assembly.", v => runner.TestAssembly = Path.GetFullPath(v)},
                {"r:|results:", "The path to the results file.", v=>runner.Results = Path.GetFullPath(v)},
                {"f:|fixture:", "The full name (with namespace) of the test fixture.", v => runner.Fixture = v},
                {"t:|testName:", "The name of a test to run", v => runner.Test = v},
                {"c:|concatenate:", "Concatenate results with existing results file.", v=> runner.Concat = v != null},
                {"gui:", "Show the revit test runner gui.", v=>runner.Gui = v != null},
                {"d|debug", "Run in debug mode.", v=>runner.IsDebug = v != null},
                {"h|help", "Show this message and exit.", v=> showHelp = v != null}
            };

            var notParsed = new List<string>();

            const string helpMessage = "Try 'DynamoTestFrameworkRunner --help' for more information.";

            try
            {
                notParsed = p.Parse(args);
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(helpMessage);
                return false;
            }

            if (notParsed.Count > 0)
            {
                Console.WriteLine(String.Join(" ", notParsed.ToArray()));
                return false;
            }

            if (showHelp)
            {
                ShowHelp(p);
                return false;
            }

            if (!String.IsNullOrEmpty(runner.TestAssembly) && !File.Exists(runner.TestAssembly))
            {
                Console.Write("The specified test assembly does not exist.");
                return false;
            }

            if (!String.IsNullOrEmpty(runner.WorkingDirectory) && !Directory.Exists(runner.WorkingDirectory))
            {
                Console.Write("The specified working directory does not exist.");
                return false;
            }

            return true;
        }

        private static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: DynamoTestFrameworkRunner [OPTIONS]");
            Console.WriteLine("Run a test or a fixture of tests from an assembly.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

    }
}
