using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Newtonsoft.Json;
using System.Data.SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Dynamic;


namespace GTTTimeImporter
{


    public class GTTTimeImporter : Plugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        string _SQLITE_DB_LOCATION = "c:/ProgramData/Gameplay Time Tracker/UserData/Profiles/User/GameplayTimeTracker.sqlite";
        string _SQL = "select ProductName, StatTotalFullRuntime/10000000.0 as RuntimeSeconds, StatRunCount, StatLastRunDateTimeUtc from Applications where StatTotalFullRuntime > 1000;";
        // string _LOCALCONFIG_VDF = "c:/Program Files (x86)/Steam/userdata/10065683/config/localconfig.vdf";

        private GTTTimeImporterSettings settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("65e42ff8-fa71-4239-bc89-f31aed581c21");

        public GTTTimeImporter(IPlayniteAPI api) : base(api)
        {
            settings = new GTTTimeImporterSettings(this);
        }

        public override void OnGameInstalled(Game game)
        {
            // Add code to be executed when game is finished installing.
        }

        public override void OnGameStarted(Game game)
        {
            // Add code to be executed when game is started running.
        }

        public override void OnGameStarting(Game game)
        {
            // Add code to be executed when game is preparing to be started.
        }

        public override void OnGameStopped(Game game, long elapsedSeconds)
        {
            // Add code to be executed when game is preparing to be started.
        }

        public override void OnGameUninstalled(Game game)
        {
            // Add code to be executed when game is uninstalled.
        }

        public override void OnApplicationStarted()
        {
            // Add code to be executed when Playnite is initialized.
        }

        public override void OnApplicationStopped()
        {
            // Add code to be executed when Playnite is shutting down.
        }

        public override void OnLibraryUpdated()
        {
            // Add code to be executed when library is updated.
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

            var resultsByName = new Dictionary<string, ExpandoObject>();
            var connectionString = new SQLiteConnectionStringBuilder {DataSource=_SQLITE_DB_LOCATION};
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
                        // string _SQL = "select ProductName, StatTotalFullRuntime/10000000.0 as RuntimeSeconds, StatRunCount, StatLastRunDateTimeUtc from Applications where StatTotalFullRuntime > 1000;";

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
                    var fl = (long) Math.Floor(rhsRuntime);
                    if (fl > game.Playtime)
                    {
                        changed = true;
                        logger.Info(string.Format("old runtime {0} new runtime {1}", game.Playtime, fl));
                        game.Playtime = fl;
                    }

                    var rhsCount = sqlData.StatRunCount;
                    if (rhsCount > game.PlayCount)
                    {
                        changed = true;
                        logger.Info(string.Format("old playcount {0} new playcount {1}", game.PlayCount, rhsCount));
                        game.PlayCount = rhsCount;
                    }
                    DateTimeOffset rhsDate = sqlData.StatLastRunDateTimeUtc;
                    if (game.LastActivity < rhsDate.LocalDateTime)
                    {
                        changed = true;
                        logger.Info(string.Format("old last activity {0} new last activity {1}", game.LastActivity, rhsDate.LocalDateTime));
                        game.LastActivity = rhsDate.LocalDateTime;

                    }

                    if (changed)
                    {
                        logger.Info(string.Format("changed {0}", game.Name));
                        PlayniteApi.Database.Games.Update(game);
                    }

                }

            }



        }

        // To add new main menu items override GetMainMenuItems
        public override List<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args2)
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
    }
}