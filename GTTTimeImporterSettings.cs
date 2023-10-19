using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTTTimeImporter
{
    public class GTTTimeImporterSettings : ObservableObject
    {
        private string shortcutsVdfPath = string.Empty;
        private string gttDbPath = string.Empty;

        private string gogGalaxyPath = @"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe";

        private bool useGogGalaxy = false;
        private bool updateGttDb = false;
        public string ShortcutsVdfPath { get => shortcutsVdfPath; set => SetValue(ref shortcutsVdfPath, value); }
        public string GTTDbPath { get => gttDbPath; set => SetValue(ref gttDbPath, value); }

        public bool UseGogGalaxy { get => useGogGalaxy; set => SetValue(ref useGogGalaxy, value); }

        public string GogGalaxyPath { get => gogGalaxyPath; set => SetValue(ref gogGalaxyPath, value); }

        public bool UpdateGttDb { get => updateGttDb; set => SetValue(ref updateGttDb, value);}


    }

    public class GTTTimeImporterSettingsViewModel : ObservableObject, ISettings
    {
        private readonly GTTTimeImporter plugin;
        private GTTTimeImporterSettings editingClone { get; set; }

        private GTTTimeImporterSettings settings;
        public GTTTimeImporterSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public GTTTimeImporterSettingsViewModel(GTTTimeImporter plugin)
        {
            // Injecting your plugin instance is required for Save/Load method because Playnite saves data to a location based on what plugin requested the operation.
            this.plugin = plugin;

            // Load saved settings.
            var savedSettings = plugin.LoadPluginSettings<GTTTimeImporterSettings>();

            // LoadPluginSettings returns null if not saved data is available.
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new GTTTimeImporterSettings();
            }
        }

        public void BeginEdit()
        {
            // Code executed when settings view is opened and user starts editing values.
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            // Code executed when user decides to cancel any changes made since BeginEdit was called.
            // This method should revert any changes made to Option1 and Option2.
            Settings = editingClone;
        }

        public void EndEdit()
        {
            // Code executed when user decides to confirm changes made since BeginEdit was called.
            // This method should save settings made to Option1 and Option2.
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            // Code execute when user decides to confirm changes made since BeginEdit was called.
            // Executed before EndEdit is called and EndEdit is not called if false is returned.
            // List of errors is presented to user if verification fails.
            errors = new List<string>();
            return true;
        }
    }
}