﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Plugin.Program.Logger;
using Flow.Launcher.Plugin.SharedCommands;
using Flow.Launcher.Plugin.SharedModels;
using Flow.Launcher.Infrastructure.Logger;
using System.Collections;
using System.Diagnostics;
using Stopwatch = Flow.Launcher.Infrastructure.Stopwatch;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Flow.Launcher.Infrastructure.Image;
using IniParser;

namespace Flow.Launcher.Plugin.Program.Programs
{
    [Serializable]
    public class Win32 : IProgram, IEquatable<Win32>
    {
        public string Name { get; set; }
        public string UniqueIdentifier { get; set; }
        public string IcoPath { get; set; }
        public string FullPath { get; set; }
        public string LnkResolvedPath { get; set; }
        public string ParentDirectory { get; set; }
        public string ExecutableName { get; set; }
        public string Description { get; set; }
        public bool Valid { get; set; }
        public bool Enabled { get; set; }
        public string Location => ParentDirectory;

        private const string ShortcutExtension = "lnk";
        private const string UrlExtension = "url";
        private const string ExeExtension = "exe";

        private static readonly Win32 Default = new Win32()
        {
            Name = string.Empty,
            Description = string.Empty,
            IcoPath = string.Empty,
            FullPath = string.Empty,
            LnkResolvedPath = null,
            ParentDirectory = string.Empty,
            ExecutableName = null,
            UniqueIdentifier = string.Empty,
            Valid = false,
            Enabled = false
        };


        public Result Result(string query, IPublicAPI api)
        {
            string title;
            MatchResult matchResult;

            // We suppose Name won't be null
            if (!Main._settings.EnableDescription || Description == null || Name.StartsWith(Description))
            {
                title = Name;
                matchResult = StringMatcher.FuzzySearch(query, title);
            }
            else if (Description.StartsWith(Name))
            {
                title = Description;
                matchResult = StringMatcher.FuzzySearch(query, Description);
            }
            else
            {
                title = $"{Name}: {Description}";
                var nameMatch = StringMatcher.FuzzySearch(query, Name);
                var desciptionMatch = StringMatcher.FuzzySearch(query, Description);
                if (desciptionMatch.Score > nameMatch.Score)
                {
                    for (int i = 0; i < desciptionMatch.MatchData.Count; i++)
                    {
                        desciptionMatch.MatchData[i] += Name.Length + 2; // 2 is ": "
                    }
                    matchResult = desciptionMatch;
                }
                else matchResult = nameMatch;
            }

            if (!matchResult.IsSearchPrecisionScoreMet())
            {
                if (ExecutableName != null) // only lnk program will need this one
                    matchResult = StringMatcher.FuzzySearch(query, ExecutableName);

                if (!matchResult.IsSearchPrecisionScoreMet())
                    return null;

                matchResult.MatchData = new List<int>();
            }

            var result = new Result
            {
                Title = title,
                SubTitle = Main._settings.HideAppsPath ? string.Empty : LnkResolvedPath ?? FullPath,
                IcoPath = IcoPath,
                Score = matchResult.Score,
                TitleHighlightData = matchResult.MatchData,
                ContextData = this,
                Action = c =>
                {
                    var runAsAdmin = (
                        c.SpecialKeyState.CtrlPressed &&
                        c.SpecialKeyState.ShiftPressed &&
                        !c.SpecialKeyState.AltPressed &&
                        !c.SpecialKeyState.WinPressed
                        );

                    var info = new ProcessStartInfo
                    {
                        FileName = LnkResolvedPath ?? FullPath,
                        WorkingDirectory = ParentDirectory,
                        UseShellExecute = true,
                        Verb = runAsAdmin ? "runas" : null
                    };

                    Task.Run(() => Main.StartProcess(Process.Start, info));

                    return true;
                }
            };

            return result;
        }


        public List<Result> ContextMenus(IPublicAPI api)
        {
            var contextMenus = new List<Result>
            {
                new Result
                {
                    Title = api.GetTranslation("flowlauncher_plugin_program_run_as_different_user"),
                    Action = _ =>
                    {
                        var info = new ProcessStartInfo
                        {
                            FileName = FullPath,
                            WorkingDirectory = ParentDirectory,
                            UseShellExecute = true
                        };

                        Task.Run(() => Main.StartProcess(ShellCommand.RunAsDifferentUser, info));

                        return true;
                    },
                    IcoPath = "Images/user.png",
                    Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\xe7ee"),
                },
                new Result
                {
                    Title = api.GetTranslation("flowlauncher_plugin_program_run_as_administrator"),
                    Action = _ =>
                    {
                        var info = new ProcessStartInfo
                        {
                            FileName = FullPath,
                            WorkingDirectory = ParentDirectory,
                            Verb = "runas",
                            UseShellExecute = true
                        };

                        Task.Run(() => Main.StartProcess(Process.Start, info));

                        return true;
                    },
                    IcoPath = "Images/cmd.png",
                    Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\xe7ef"),
                },
                new Result
                {
                    Title = api.GetTranslation("flowlauncher_plugin_program_open_containing_folder"),
                    Action = _ =>
                    {
                        Main.Context.API.OpenDirectory(ParentDirectory, FullPath);

                        return true;
                    },
                    IcoPath = "Images/folder.png",
                    Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\xe838"),
                }
            };
            return contextMenus;
        }


        public override string ToString()
        {
            return Name;
        }

        public static List<FileSystemWatcher> Watchers = new List<FileSystemWatcher>();


        private static Win32 Win32Program(string path)
        {
            try
            {
                var p = new Win32
                {
                    Name = Path.GetFileNameWithoutExtension(path),
                    IcoPath = path,
                    FullPath = path,
                    UniqueIdentifier = path,
                    ParentDirectory = Directory.GetParent(path).FullName,
                    Description = string.Empty,
                    Valid = true,
                    Enabled = true
                };
                return p;
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException)
            {
                ProgramLogger.LogException($"|Win32|Win32Program|{path}" +
                                           $"|Permission denied when trying to load the program from {path}", e);

                return new Win32()
                {
                    Valid = false, Enabled = false
                };
            }
        }

        private static Win32 LnkProgram(string path)
        {
            var program = Win32Program(path);
            try
            {
                const int MAX_PATH = 260;
                StringBuilder buffer = new StringBuilder(MAX_PATH);
                ShellLinkHelper _helper = new ShellLinkHelper();
                string target = _helper.retrieveTargetPath(path);

                if (!string.IsNullOrEmpty(target))
                {
                    var extension = Extension(target);
                    if (extension == ExeExtension && File.Exists(target))
                    {
                        program.LnkResolvedPath = program.FullPath;
                        program.FullPath = Path.GetFullPath(target).ToLower();
                        program.ExecutableName = Path.GetFileName(target);

                        var description = _helper.description;
                        if (!string.IsNullOrEmpty(description))
                        {
                            program.Description = description;
                        }
                        else
                        {
                            var info = FileVersionInfo.GetVersionInfo(target);
                            if (!string.IsNullOrEmpty(info.FileDescription))
                            {
                                program.Description = info.FileDescription;
                            }
                        }
                    }
                }

                return program;
            }
            catch (COMException e)
            {
                // C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\MiracastView.lnk always cause exception
                ProgramLogger.LogException($"|Win32|LnkProgram|{path}" +
                                           "|Error caused likely due to trying to get the description of the program",
                    e);

                program.Valid = false;
                return program;
            }
#if !DEBUG //Only do a catch all in production. This is so make developer aware of any unhandled exception and add the exception handling in.
            catch (Exception e)
            {
                ProgramLogger.LogException($"|Win32|LnkProgram|{path}" +
                                                "|An unexpected error occurred in the calling method LnkProgram", e);

                program.Valid = false;
                return program;
            }
#endif
        }

        private static Win32 UrlProgram(string path)
        {
            var program = Win32Program(path);
            program.Valid = false;

            try
            {
                var parser = new FileIniDataParser();
                var data = parser.ReadFile(path);
                var urlSection = data["InternetShortcut"];
                var url = urlSection?["URL"];
                if (String.IsNullOrEmpty(url))
                {
                    return program;
                }
                foreach(var protocol in Main._settings.GetProtocols())
                {
                    if(url.StartsWith(protocol))
                    {
                        program.LnkResolvedPath = url;
                        program.Valid = true;
                        break;
                    }
                }

                var iconPath = urlSection?["IconFile"];
                if (!String.IsNullOrEmpty(iconPath))
                {
                    program.IcoPath = iconPath;
                }
            }
            catch (Exception e)
            {
                // Many files do not have the required fields, so no logging is done.
            }

            return program;
        }

        private static Win32 ExeProgram(string path)
        {
            try
            {
                var program = Win32Program(path);
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrEmpty(info.FileDescription))
                    program.Description = info.FileDescription;
                return program;
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException)
            {
                ProgramLogger.LogException($"|Win32|ExeProgram|{path}" +
                                           $"|Permission denied when trying to load the program from {path}", e);

                return new Win32()
                {
                    Valid = false, Enabled = false
                };
            }
        }

        private static IEnumerable<string> ProgramPaths(string directory, string[] suffixes)
        {
            if (!Directory.Exists(directory))
                return Enumerable.Empty<string>();

            return Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true, RecurseSubdirectories = true
            }).Where(x => suffixes.Contains(Extension(x)));
        }

        private static string Extension(string path)
        {
            var extension = Path.GetExtension(path)?.ToLower();
            if (!string.IsNullOrEmpty(extension))
            {
                return extension.Substring(1);
            }
            else
            {
                return string.Empty;
            }
        }

        private static IEnumerable<Win32> UnregisteredPrograms(List<Settings.ProgramSource> sources, string[] suffixes)
        {
            var paths = ExceptDisabledSource(sources.Where(s => Directory.Exists(s.Location) && s.Enabled)
                    .SelectMany(s => ProgramPaths(s.Location, suffixes)), x => x)
                .Distinct();

            var programs = paths.Select(x => Extension(x) switch
            {
                ExeExtension => ExeProgram(x),
                ShortcutExtension => LnkProgram(x),
                UrlExtension => UrlProgram(x),
                _ => Win32Program(x)
            });


            return programs;
        }

        private static IEnumerable<Win32> StartMenuPrograms(string[] suffixes)
        {
            var disabledProgramsList = Main._settings.DisabledProgramSources;

            var directory1 = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var directory2 = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var paths1 = ProgramPaths(directory1, suffixes);
            var paths2 = ProgramPaths(directory2, suffixes);

            var toFilter = paths1.Concat(paths2);

            var programs = ExceptDisabledSource(toFilter.Distinct())
                .Select(x => Extension(x) switch
                {
                    ShortcutExtension => LnkProgram(x),
                    UrlExtension => UrlProgram(x),
                    _ => Win32Program(x)
                }).Where(x => x.Valid);
            return programs;
        }

        private static IEnumerable<Win32> AppPathsPrograms(string[] suffixes)
        {
            // https://msdn.microsoft.com/en-us/library/windows/desktop/ee872121
            const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

            IEnumerable<string> toFilter = Enumerable.Empty<string>();

            using var rootMachine = Registry.LocalMachine.OpenSubKey(appPaths);
            using var rootUser = Registry.CurrentUser.OpenSubKey(appPaths);

            if (rootMachine != null)
            {
                toFilter = toFilter.Concat(GetPathFromRegistry(rootMachine));
            }

            if (rootUser != null)
            {
                toFilter = toFilter.Concat(GetPathFromRegistry(rootUser));
            }


            toFilter = toFilter.Distinct().Where(p => suffixes.Contains(Extension(p)));

            var filtered = ExceptDisabledSource(toFilter);

            return filtered.Select(GetProgramFromPath).ToList(); // ToList due to disposing issue
        }

        private static IEnumerable<string> GetPathFromRegistry(RegistryKey root)
        {
            return root
                .GetSubKeyNames()
                .Select(x => GetProgramPathFromRegistrySubKeys(root, x))
                .Distinct();
        }

        private static string GetProgramPathFromRegistrySubKeys(RegistryKey root, string subkey)
        {
            var path = string.Empty;
            try
            {
                using (var key = root.OpenSubKey(subkey))
                {
                    if (key == null)
                        return string.Empty;

                    var defaultValue = string.Empty;
                    path = key.GetValue(defaultValue) as string;
                }

                if (string.IsNullOrEmpty(path))
                    return string.Empty;

                // fix path like this: ""\"C:\\folder\\executable.exe\""
                return path = path.Trim('"', ' ');
            }
            catch (Exception e) when (e is SecurityException || e is UnauthorizedAccessException)
            {
                ProgramLogger.LogException($"|Win32|GetProgramPathFromRegistrySubKeys|{path}" +
                                           $"|Permission denied when trying to load the program from {path}", e);

                return string.Empty;
            }
        }

        private static Win32 GetProgramFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Default;

            path = Environment.ExpandEnvironmentVariables(path);

            if (!File.Exists(path))
                return Default;

            var entry = Win32Program(path);

            return entry;
        }

        public static IEnumerable<string> ExceptDisabledSource(IEnumerable<string> sources)
        {
            return ExceptDisabledSource(sources, x => x);
        }

        public static IEnumerable<TSource> ExceptDisabledSource<TSource>(IEnumerable<TSource> sources,
            Func<TSource, string> keySelector)
        {
            return Main._settings.DisabledProgramSources.Count == 0
                ? sources
                : ExceptDisabledSourceEnumerable(sources, keySelector);

            static IEnumerable<TSource> ExceptDisabledSourceEnumerable(IEnumerable<TSource> elements,
                Func<TSource, string> selector)
            {
                var set = Main._settings.DisabledProgramSources.Select(x => x.UniqueIdentifier).ToHashSet();

                foreach (var element in elements)
                {
                    if (!set.Contains(selector(element)))
                        yield return element;
                }
            }
        }

        public static IEnumerable<T> DistinctBy<T, R>(IEnumerable<T> source, Func<T, R> selector)
        {
            var set = new HashSet<R>();
            foreach (var item in source)
            {
                if (set.Add(selector(item)))
                    yield return item;
            }
        }

        private static IEnumerable<Win32> ProgramsHasher(IEnumerable<Win32> programs)
        {
            return programs.GroupBy(p => p.FullPath.ToLower())
                .SelectMany(g =>
                {
                    var temp = g.Where(g => !string.IsNullOrEmpty(g.Description)).ToList();
                    if (temp.Any())
                        return DistinctBy(temp, x => x.Description);
                    return g.Take(1);
                }).ToArray();
        }


        public static Win32[] All(Settings settings)
        {
            try
            {
                var programs = Enumerable.Empty<Win32>();

                var unregistered = UnregisteredPrograms(settings.ProgramSources, settings.GetSuffixes());

                programs = programs.Concat(unregistered);

                var autoIndexPrograms = Enumerable.Empty<Win32>();

                if (settings.EnableRegistrySource)
                {
                    var appPaths = AppPathsPrograms(settings.GetSuffixes());
                    autoIndexPrograms = autoIndexPrograms.Concat(appPaths);
                }

                if (settings.EnableStartMenuSource)
                {
                    var startMenu = StartMenuPrograms(settings.GetSuffixes());
                    autoIndexPrograms = autoIndexPrograms.Concat(startMenu);
                }

                autoIndexPrograms = ProgramsHasher(autoIndexPrograms);

                return programs.Concat(autoIndexPrograms).Distinct().ToArray();
            }
#if DEBUG //This is to make developer aware of any unhandled exception and add in handling.
            catch (Exception)
            {
                throw;
            }
#endif

#if !DEBUG //Only do a catch all in production.
            catch (Exception e)
            {
                ProgramLogger.LogException("|Win32|All|Not available|An unexpected error occurred", e);

                return Array.Empty<Win32>();
            }
#endif
        }

        public override int GetHashCode()
        {
            return UniqueIdentifier.GetHashCode();
        }

        public bool Equals([AllowNull] Win32 other)
        {
            if (other == null)
                return false;

            return UniqueIdentifier == other.UniqueIdentifier;
        }

        private static IEnumerable<string> GetStartMenuPaths()
        {
            var directory1 = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var directory2 = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            return new[]
            {
                directory1, directory2
            };
        }

        public static void WatchProgramUpdate(Settings settings)
        {
            var paths = new List<string>();
            if (settings.EnableStartMenuSource)
                paths.AddRange(GetStartMenuPaths());

            paths.AddRange(from source in settings.ProgramSources where source.Enabled select source.Location);

            foreach (var directory in from path in paths where Directory.Exists(path) select path)
            {
                WatchDirectory(directory);
            }

            _ = Task.Run(MonitorDirectoryChangeAsync);
        }

        private static Channel<byte> indexQueue = Channel.CreateBounded<byte>(1);

        public static async Task MonitorDirectoryChangeAsync()
        {
            var reader = indexQueue.Reader;
            while (await reader.WaitToReadAsync())
            {
                await Task.Delay(500);
                while (reader.TryRead(out _))
                {
                }
                await Task.Run(Main.IndexWin32Programs);
            }
        }
        
        public static void WatchDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                throw new ArgumentException("Path Not Exist");
            }
            var watcher = new FileSystemWatcher(directory);

            watcher.Created += static (_, _) => indexQueue.Writer.TryWrite(default);
            watcher.Deleted += static (_, _) => indexQueue.Writer.TryWrite(default);
            watcher.EnableRaisingEvents = true;
            watcher.IncludeSubdirectories = true;
            
            Watchers.Add(watcher);
        }

        public static void Dispose()
        {
            foreach (var fileSystemWatcher in Watchers)
            {
                fileSystemWatcher.Dispose();
            }
        }
    }
}
