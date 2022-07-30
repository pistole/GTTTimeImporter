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
using System.Data.HashFunction.CRC;
using System.IO;


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
            foreach(var action in game.OtherActions)
            {
                logger.Info("otherAction" + action.ToString());
                if (action.Name == "Launch without Steam")
                {
                    return action;
                }
            }
            var defaultAction = game.PlayAction;
            if (defaultAction.Type == GameActionType.File)
            {
                return defaultAction;
            }
            foreach(var action in game.OtherActions)
            {
                if (action.Type == GameActionType.File && action.Name != "Settings")
                {
                    return action;
                }
            }
            return null;

        }

        private VDFParser.Models.VDFEntry CreateEntry(Game game)
        {
            // 29-07 19:37:51.143|INFO|GTTTimeImporter:[VDFEntry: AppName=Amatsutsumi, Exe="D:\games\gog\Amatsutsumi\cmvs64.exe", StartDir="D:\games\gog\Amatsutsumi", Icon=d:\games\gog\Amatsutsumi\Amatsutsumi-banner-c868528cd30966b87e384bd26c22.jpg, ShortcutPath=, LaunchOptions=, IsHidden=0, AllowDesktopConfig=1, AllowOverlay=1, OpenVR=0, Devkit=0, DevkitGameID=, LastPlayTime=1659141198, Tags=GOG.com, sekai, Visual Novel, r18-patched, Non STEAM, Sketch, VOICED VNS]
            var action = GetRunAction(game);
            logger.Info(action.ToString());
            if (action == null)
            {
                return null;
            }
            action = PlayniteApi.ExpandGameVariables(game, action);
            var entry = new VDFParser.Models.VDFEntry();
            entry.AppName = game.Name;
            entry.Exe = action.Path;
            entry.StartDir = game.InstallDirectory;
            entry.Icon = game.GameImagePath + "\\" + game.BackgroundImage;
            entry.DevkitGameID = string.Format( "playnite-{0}", game.Id);
            entry.Tags = game.Categories.Select(x => x.Name).ToArray();
            entry.LaunchOptions = action.AdditionalArguments;
            entry.LastPlayTime = game.LastActivity.HasValue ? (int) Math.Floor((game.LastActivity.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds) : entry.LastPlayTime;
            logger.Info(entry.ToString());
            return entry;
        } 

        private string ShortcutsVdfPath
        {
            get 
            {   
                // fixme
                return "c:\\Program Files (x86)\\Steam\\userdata\\10065683\\config\\shortcuts.vdf";
            }
        }

        private void AddToSteam(GameMenuItemActionArgs args)
        {
            var parsed = VDFParser.VDFParser.Parse(ShortcutsVdfPath).ToList();
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
                logger.Info(goldenEntry.ToString());
                if (goldenEntry == null)
                {
                    isNew = true;
                    goldenEntry = CreateEntry(game);
                }

                logger.Info(game.ToString());
                var stringToCrc = goldenEntry.Exe + goldenEntry.AppName;
                logger.Info(stringToCrc);
                var hashed = CalculateHashLower(stringToCrc);
                logger.Info((hashed << 32).ToString());
                hashed = (hashed << 32) | 0x02000000;
                logger.Info(hashed.ToString());
                var steamUrl = "steam://rungameid/" + hashed.ToString();
                var action = GetRunAction(game);
                if (action == null)
                {
                    throw new Exception("unable to find executable play action!");
                }
                logger.Info(action.ToString());
                logger.Info(CreateEntry(game).ToString());
                if (action == game.PlayAction)
                {
                    logger.Info("Create New playAction");
                    // create new action
                    var newAction = new GameAction();
                    newAction.Type = GameActionType.URL;
                    newAction.Arguments = steamUrl;
                    
                    // move old action to other actions with title "Launch without Steam"
                    var oldAction = game.PlayAction;
                    oldAction.Name = "Launch without Steam";
                    game.OtherActions.Add(oldAction);
                    game.PlayAction = newAction;
                }
                logger.Info("isNew " + isNew.ToString());

                if (isNew)
                {
                    parsed.Add(goldenEntry);
                }
                // write out shortcuts
                var output = VDFParser.VDFSerializer.Serialize(parsed.ToArray());
                var backupPath = ShortcutsVdfPath.Replace(".vdf", "-bak.rhp.vdf");
                File.Move(ShortcutsVdfPath, backupPath);
                File.WriteAllBytes(ShortcutsVdfPath, output);
                // update game db
                PlayniteApi.Database.Games.Update(game);
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

        public override List<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args2)
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