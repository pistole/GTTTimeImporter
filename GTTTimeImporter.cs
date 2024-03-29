﻿using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Newtonsoft.Json;
using System.Data.SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Dynamic;
using System.Data.HashFunction.CRC;
using System.IO;
using System.Collections.ObjectModel;


namespace GTTTimeImporter
{


    public class GTTTimeImporter : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        string _SQL = "select Id, ProductName, StatTotalFullRuntime/10000000.0 as RuntimeSeconds, StatRunCount, StatLastRunDateTimeUtc from Applications where StatTotalFullRuntime > 1000;";

        private GTTTimeImporterSettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("d9388873-be1a-47a8-ab37-7e6aeb62e435");

        public GTTTimeImporter(IPlayniteAPI api) : base(api)
        {
            settings = new GTTTimeImporterSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        private string gridPath
        {
            get
            {
                return settings.Settings.ShortcutsVdfPath.Replace("shortcuts.vdf", "") + "grid\\";
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new GTTTimeImporterSettingsView();
        }
        private string Slugify(string name)
        {
            var retval = Regex.Replace(name, "[^A-Za-z0-9.]", "_").ToLower();
            retval = Regex.Replace(retval, "_+", ".").TrimEnd(new char[] {'.'});
            return retval;
        }

        // private string[] IgnoredPlatforms = new string [] {"PlayStation"};

        private IDictionary<string, Game> GatherGames(MainMenuItemActionArgs args)
        {
            var retval = new Dictionary<string, Game>();
            foreach (var game in PlayniteApi.Database.Games)
            {
                if (game.InstallationStatus != InstallationStatus.Installed)
                {
                    continue;
                }
                var nameSlug = Slugify(game.Name);
                if (retval.ContainsKey(nameSlug))
                {
                    var existing = retval[nameSlug];

                    logger.Info(string.Format("{0} existing {1} new source {2}", game.Name, existing.Source, game.Source));
                    if (existing.Source != null && existing.Source.Name == "Steam")
                    {
                        continue;
                    }
                    if (game.Source != null && (game.Source.Name == "Ubisoft Connect" || game.Source.Name == "Uplay"))
                    {
                        continue;
                    }
                    if (game.Source == existing.Source && existing.Playtime > game.Playtime)
                    {
                        continue;
                    }
                    logger.Info("picking new");

                }
                retval[nameSlug] = game;
            }
            return retval;
        }

        private ExpandoObject ParseRow(System.Collections.Specialized.NameValueCollection row)
        {
            dynamic retval = new ExpandoObject();
            retval.Id = row["Id"];
            retval.ProductName = row["ProductName"];
            retval.RuntimeSeconds = float.Parse(row["RuntimeSeconds"]);
            retval.StatRunCount = int.Parse(row["StatRunCount"]);
            retval.StatLastRunDateTimeUtc = DateTimeOffset.Parse(row["StatLastRunDateTimeUtc"]);
            return retval;

        }

        private void DoTheThing(MainMenuItemActionArgs args)
        {

            var changed = false;
            var logger = LogManager.GetLogger();
            var games = GatherGames(args);
            var sqlUpdateData = new List<ExpandoObject>();

            var resultsByName = new Dictionary<string, ExpandoObject>();
            var connectionString = new SQLiteConnectionStringBuilder {DataSource=settings.Settings.GTTDbPath};
            using (var connection = new SQLiteConnection(connectionString.ToString()))
            {
                connection.Open();
                var command = new SQLiteCommand(connection);
                command.CommandText = _SQL;
                using (var reader = command.ExecuteReader())
                {
                    while(reader.Read())
                    {
                        var vals = reader.GetValues();
                        var name = vals["ProductName"];
                        name = Slugify(name);
                        dynamic parsedRow = ParseRow(vals);
                        // string _SQL = "select Id, ProductName, StatTotalFullRuntime/10000000.0 as RuntimeSeconds, StatRunCount, StatLastRunDateTimeUtc from Applications where StatTotalFullRuntime > 1000;";

                        if (resultsByName.ContainsKey(name))
                        {
                            dynamic oldRow = resultsByName[name];
                            if (oldRow.RuntimeSeconds > parsedRow.RuntimeSeconds)
                            {
                                continue;
                            }
                        }
                        resultsByName[name] = parsedRow;
                        logger.Info(name);
                        // logger.Info(string.Join(", ", vals.GetValues(null)));
                        var rowDict = (IDictionary<String, Object>) parsedRow;
                        logger.Info(JsonConvert.SerializeObject(rowDict, Formatting.Indented));
                    }
                }
                connection.Close();
            }
            foreach (var nameSlug in resultsByName.Keys)
            {
                changed = false;
                if (games.ContainsKey(nameSlug))
                {
                    var game = games[nameSlug];
                    dynamic sqlData = resultsByName[nameSlug];
                    logger.Info(string.Format("Matched playnite {0} to GTT {1}", game.Name, sqlData.ProductName));
                    var rhsRuntime = (float) sqlData.RuntimeSeconds;
                    var fl = (ulong) Math.Floor(rhsRuntime);
                    dynamic sqlOutputData = new ExpandoObject();
                    sqlOutputData.Id = sqlData.Id;
                    var sqlChanged = false;
                    sqlOutputData.RuntimeSeconds = rhsRuntime;
                    if (fl > game.Playtime)
                    {
                        changed = true;
                        logger.Info(string.Format("playnite runtime {0} gtt runtime {1}", game.Playtime, fl));
                        game.Playtime = fl;
                    }
                    else if (fl < game.Playtime)
                    {
                        sqlChanged = true;
                        sqlOutputData.RuntimeSeconds = game.Playtime;
                        logger.Info(string.Format("playnite runtime {0} gtt runtime {1}", game.Playtime, fl));
                    }

                    var rhsCount = (ulong) sqlData.StatRunCount;
                    sqlOutputData.StatRunCount = rhsCount;

                    if (rhsCount > game.PlayCount)
                    {
                        changed = true;
                        logger.Info(string.Format("playnite playcount {0} gtt playcount {1}", game.PlayCount, rhsCount));
                        game.PlayCount = rhsCount;
                    }
                    else if (rhsCount < game.PlayCount)
                    {
                        sqlChanged = true;
                        sqlOutputData.StatRunCount = game.PlayCount;
                        logger.Info(string.Format("playnite playcount {0} gtt playcount {1}", game.PlayCount, rhsCount));
                    }

                    DateTimeOffset rhsDate = sqlData.StatLastRunDateTimeUtc;
                    sqlOutputData.StatLastRunDateTimeUtc = rhsDate;
                    if (game.LastActivity < rhsDate.LocalDateTime)
                    {
                        changed = true;
                        logger.Info(string.Format("playnite last activity {0} gtt last activity {1}", game.LastActivity, rhsDate.LocalDateTime));
                        game.LastActivity = rhsDate.LocalDateTime;

                    }
                    else if (game.LastActivity > rhsDate.LocalDateTime)
                    {
                        sqlChanged = true;
                        sqlOutputData.StatLastRunDateTimeUtc = game.LastActivity?.ToUniversalTime() ?? rhsDate.LocalDateTime;
                        logger.Info(string.Format("playnite last activity {0} gtt last activity {1}", game.LastActivity, rhsDate.LocalDateTime));
                    }

                    if (changed)
                    {
                        logger.Info(string.Format("changed {0}", game.Name));
                        PlayniteApi.Database.Games.Update(game);
                    }
                    if (sqlChanged)
                    {
                        logger.Info(string.Format("sqlChanged {0} {1}", game.Name, sqlData.Id));
                        sqlUpdateData.Add(sqlOutputData);
                    }

                }
                if (settings.Settings.UpdateGttDb && sqlUpdateData.Count > 0)
                {
                    using (var connection = new SQLiteConnection(connectionString.ToString()))
                    {
                        connection.Open();
                        var command = new SQLiteCommand(connection);
                        command.CommandText = "update Applications set StatTotalFullRuntime = @RuntimeSeconds, StatRunCount = @StatRunCount, StatLastRunDateTimeUtc = @StatLastRunDateTimeUtc where Id = @Id;";
                        command.Parameters.Add(new SQLiteParameter("@RuntimeSeconds"));
                        command.Parameters.Add(new SQLiteParameter("@StatRunCount"));
                        command.Parameters.Add(new SQLiteParameter("@StatLastRunDateTimeUtc"));
                        command.Parameters.Add(new SQLiteParameter("@Id"));
                        foreach (dynamic row in sqlUpdateData)
                        {
                            command.Parameters["@RuntimeSeconds"].Value = row.RuntimeSeconds * 10000000;
                            command.Parameters["@StatRunCount"].Value = row.StatRunCount;
                            command.Parameters["@StatLastRunDateTimeUtc"].Value = row.StatLastRunDateTimeUtc.ToString("o").Replace("T", " ");
                            command.Parameters["@Id"].Value = row.Id;
                            logger.Info("executing " + command.CommandText + " " + string.Join(", ", command.Parameters.Cast<SQLiteParameter>().Select(x => x.ParameterName + "=" + x.Value.ToString())));
                            command.ExecuteNonQuery();
                        }
                    connection.Close();
                    }
                }
            }



        }

        private ulong CalculateHashLower(string input)
        {
            var streamToCrc =  new MemoryStream(Encoding.UTF8.GetBytes(input));
            // var crcConfig = new CRCConfig {
            //     HashSizeInBits=32,
            //     Polynomial=0x04C11DB7,
            //     ReflectIn=true,
            //     InitialValue=0xFFFFFFFF,
            //     ReflectOut=true,
            //     XOrOut=0xFFFFFFFF,
            // };
            var algorithm = CRCFactory.Instance.Create(CRCConfig.CRC32);
            var output = algorithm.ComputeHash(streamToCrc);
            ulong hashed = 0x0L;
            foreach (var curr in output.Hash.Reverse())
            {
                logger.Info("before " + curr.ToString() + " " + hashed.ToString());
                hashed = hashed << 8;
                hashed = (hashed | curr);
                logger.Info("after " + curr.ToString() + " " + hashed.ToString());

            }
            logger.Info(hashed.ToString());
            hashed = hashed  | 0x80000000;
            return hashed;
        }

        private GameAction GetRunAction(Game game)
        {
            logger.Info("game.IncludeLibraryPluginAction " + game.IncludeLibraryPluginAction.ToString());
            if (game.IncludeLibraryPluginAction)
            {
                var plugin = PlayniteApi.Addons.Plugins.FirstOrDefault(x => x.Id == game.PluginId);
                if (plugin != null)
                {
                    var libraryPlugin = plugin as LibraryPlugin;
                    logger.Info(plugin.ToString());
                    logger.Info(libraryPlugin?.ToString());
                    if (settings.Settings.UseGogGalaxy && libraryPlugin != null && libraryPlugin.Name == "GOG") {
                        return new GameAction {
                                Name = game.Name,
                                Path = settings.Settings.GogGalaxyPath,
                                WorkingDir = game.InstallDirectory,
                                Arguments = string.Format(@"/gameId={0} /command=runGame /path=""{1}""", game.GameId, game.InstallDirectory),
                                Type = GameActionType.File,
                                IsPlayAction = true
                            };
                    } else {
                        try {
                            var controllerAction = plugin.GetPlayActions(new GetPlayActionsArgs { Game = game}).First();
                            var automaticController = (AutomaticPlayController) controllerAction;
                            return new GameAction {
                                Name = automaticController.Name,
                                Path = automaticController.Path,
                                WorkingDir = automaticController.WorkingDir,
                                Arguments = automaticController.Arguments,
                                Type =  automaticController.Type == AutomaticPlayActionType.File ?  GameActionType.File : GameActionType.URL,
                                IsPlayAction = true
                            };
                        } catch (Exception e) {
                            logger.Error(e.ToString());
                            throw;
                        }
                    }
                }
            }
            foreach(var action in game.GameActions)
            {
                logger.Info("otherAction" + action.ToString());
                if (action.Name == "Launch without Steam")
                {
                    return action;
                }
            }
            foreach(var action in game.GameActions)
            {
                if (action.Type == GameActionType.File && action.Name != "Settings")
                {
                    return action;
                }
            }
            return game.GameActions.FirstOrDefault(x => x.IsPlayAction);

        }

        private VDFParser.Models.VDFEntry CreateEntry(Game game)
        {
            var action = GetRunAction(game);
            logger.Info(action.ToString());
            if (action == null)
            {
                return null;
            }
            action = PlayniteApi.ExpandGameVariables(game, action);
            var entry = new VDFParser.Models.VDFEntry();
            entry.AppName = game.Name;
            var path = action.Path;
            if (path.ToLower().EndsWith(".exe") && ! path.Contains("\\"))
            {

                path = game.InstallDirectory.TrimEnd('\\') + "\\" + path;
                if (!string.IsNullOrEmpty(action.WorkingDir))
                {
                    path = action.WorkingDir.TrimEnd('\\') + "\\" + action.Path;
                }
            }
            entry.Exe = "\"" + path + "\"";
            entry.StartDir = "\"" + game.InstallDirectory + "\"";
            if (!string.IsNullOrEmpty(action.WorkingDir))
            {
                entry.StartDir =  "\"" + action.WorkingDir + "\"";
            }

            entry.Icon = PlayniteApi.Paths.ConfigurationPath + "\\library\\files\\" + game.BackgroundImage;
            entry.DevkitGameID = string.Format( "playnite-{0}", game.Id);
            var tags = new HashSet<string>();
            if (game.Categories != null) {
                foreach (var c in game.Categories)
                {
                    tags.Add(c.Name);
                }
            }
            if (game.Genres != null) {
                foreach (var g in game.Genres)
                {
                    tags.Add(g.Name);
                }
            }
            tags.Add("Non STEAM");
            tags.Add("Installed locally");

            logger.Info("tags " + tags.Aggregate((m, n) => m + ", " + n));
            entry.Tags = tags.ToArray();

            // entry.Tags = new string[] {};
            entry.LaunchOptions = action.Arguments;
            entry.LastPlayTime = game.LastActivity.HasValue ? (int) Math.Floor((game.LastActivity.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds) : entry.LastPlayTime;
            logger.Info(entry.ToString());
            return entry;
        }

        private string ShortcutsVdfPath
        {
            get
            {
                // fixme
                return settings.Settings.ShortcutsVdfPath;
                // return "c:\\Program Files (x86)\\Steam\\userdata\\10065683\\config\\shortcuts.vdf";
            }
        }

        private void AddToSteam(GameMenuItemActionArgs args)
        {
            List<VDFParser.Models.VDFEntry> parsed = null;
            try {
                parsed = VDFParser.VDFParser.Parse(ShortcutsVdfPath).ToList();
            } catch (VDFParser.VDFTooShortException e) {
                logger.Warn("got exeption trying to parse shortcuts, will useempty list " + e.ToString());
                parsed = new List<VDFParser.Models.VDFEntry>();
            }
            logger.Info(parsed.ToString());
            bool isNew = false;
            foreach(var game in args.Games)
            {
                VDFParser.Models.VDFEntry goldenEntry = null;
                foreach(var entry in parsed)
                {
                    // logger.Info(entry.ToString());
                    if (game.Name == entry.AppName)
                    {
                        goldenEntry = entry;
                        break;
                    }
                }
                logger.Info(goldenEntry != null ? goldenEntry.ToString(): "no golden entry");
                if (goldenEntry == null)
                {
                    isNew = true;
                    goldenEntry = CreateEntry(game);
                    logger.Info(goldenEntry.ToString());
                }

                logger.Info(game.ToString());
                var stringToCrc = goldenEntry.Exe + goldenEntry.AppName;
                logger.Info(stringToCrc);
                var lower = CalculateHashLower(stringToCrc);
                logger.Info("lower " + lower.ToString());
                logger.Info((lower << 32).ToString());
                var hashed = (lower << 32) | 0x02000000;
                logger.Info(hashed.ToString());
                var steamUrl = "steam://rungameid/" + hashed.ToString();
                var action = GetRunAction(game);
                if (action == null)
                {
                    throw new Exception("unable to find executable play action!");
                }
                logger.Info(action.ToString());
                logger.Info(CreateEntry(game).ToString());
                if (game.GameActions == null || ! game.GameActions.HasItems())
                {
                    game.GameActions = new ObservableCollection<GameAction>();
                    game.IncludeLibraryPluginAction = false;
                    // create new action
                    var newAction = new GameAction();
                    newAction.Type = GameActionType.URL;
                    newAction.Path = steamUrl;
                    newAction.Name = game.Name;
                    newAction.IsPlayAction = true;

                    // move old action to other actions with title "Launch without Steam"
                    var oldAction = action;
                    oldAction.Name = "Launch without Steam";
                    oldAction.IsPlayAction = false;
                    game.GameActions.Add(oldAction);
                    game.GameActions.Insert(0, newAction);


                }
                else if (action == game.GameActions[0])
                {
                    logger.Info("Create New playAction");
                    // create new action
                    var newAction = new GameAction();
                    newAction.Type = GameActionType.URL;
                    newAction.Path = steamUrl;
                    newAction.Name = game.Name;
                    newAction.IsPlayAction = true;

                    // move old action to other actions with title "Launch without Steam"
                    var oldAction = action;
                    oldAction.Name = "Launch without Steam";
                    oldAction.IsPlayAction = false;
                    game.GameActions.Insert(0, newAction);
                }
                logger.Info("isNew " + isNew.ToString());

                if (isNew)
                {
                    goldenEntry.appid = (int)lower;
                    parsed.Add(goldenEntry);
                }
                {
                    Directory.CreateDirectory(this.gridPath);
                    logger.Info(game.CoverImage);
                    if (!string.IsNullOrEmpty(game.CoverImage))
                    {
                        var extension = game.CoverImage.Split('.').Last();
                        var dest =  gridPath + lower.ToString() + "p." + extension;
                        logger.Info(string.Format("coverImage {0}, extension {1}, dest {2} ", PlayniteApi.Paths.ConfigurationPath + "\\library\\files\\" + game.CoverImage, extension, dest));
                        if (! File.Exists(dest))
                        {
                            File.Copy(PlayniteApi.Paths.ConfigurationPath + "\\library\\files\\" + game.CoverImage, dest);
                        }
                    }
                    if (!string.IsNullOrEmpty(game.BackgroundImage))
                    {
                        goldenEntry.Icon = PlayniteApi.Paths.ConfigurationPath + "\\library\\files\\" + game.BackgroundImage;
                        var extension = game.BackgroundImage.Split('.').Last();
                        var dest =  gridPath + lower.ToString() + "." + extension;
                        logger.Info(string.Format("BackGroundImage {0}, extension {1}, dest {2} ", PlayniteApi.Paths.ConfigurationPath + "\\library\\files\\" + game.BackgroundImage, extension, dest));
                        if (! File.Exists(dest))
                        {
                            File.Copy(PlayniteApi.Paths.ConfigurationPath + "\\library\\files\\" + game.BackgroundImage, dest);
                        }
                        dest =  gridPath + lower.ToString() + "_hero." + extension;
                        if (! File.Exists(dest))
                        {
                            File.Copy(PlayniteApi.Paths.ConfigurationPath + "\\library\\files\\" + game.BackgroundImage, dest);
                        }
                    }
                }
                // write out shortcuts
                var output = VDFParser.VDFSerializer.Serialize(parsed.ToArray());
                var backupPath = ShortcutsVdfPath.Replace(".vdf", "-bak.rhp.vdf");
                File.Delete(backupPath);
                File.Move(ShortcutsVdfPath, backupPath);
                File.WriteAllBytes(ShortcutsVdfPath, output);
                // update game db
                PlayniteApi.Database.Games.Update(game);
            }
        }

        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args2)
        {
            return new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    Description = "GTT Time Importer (c#)",
                    Action = (args) => DoTheThing(args)
                }
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args2)
        {
            return new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    Description = "Add to Steam (v2)",
                    Action = (args) => AddToSteam(args)
                }
            };
        }
    }
}