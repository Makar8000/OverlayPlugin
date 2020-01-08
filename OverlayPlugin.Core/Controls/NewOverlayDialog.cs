﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RainbowMage.OverlayPlugin
{
    public partial class NewOverlayDialog : Form
    {
        public delegate bool ValidateNameDelegate(string name);

        public ValidateNameDelegate NameValidator { get; set; }
        public IOverlay SelectedOverlay { get; private set; }

        private PluginMain pluginMain;
        private IOverlay preview;

        static Dictionary<string, string> overlayNames = new Dictionary<string, string>
        {
            { "Label", Resources.MapOverlayShortLabel },
            { "MiniParse", Resources.MapOverlayShortMiniParse },
            { "SpellTimer", Resources.MapOverlayShortSpellTimer },
        };

        Dictionary<string, OverlayPreset> presets = null;

        public NewOverlayDialog(PluginMain pluginMain)
        {
            InitializeComponent();

            this.pluginMain = pluginMain;

            // Default validator
            NameValidator = (name) => { return name != null; };

            foreach (var overlayType in Registry.Overlays)
            {
                var name = overlayType.Name;
                if (name.EndsWith("Overlay"))
                {
                    name = name.Substring(0, name.Length - 7);
                }

                if (overlayNames.ContainsKey(name)) {
                    name = overlayNames[name];
                }

                cbType.Items.Add(new KeyValuePair<string, Type>(name, overlayType));
            }

            cbType.DisplayMember = "Key";
            cbType.SelectedIndex = 0;

            var presetFile = Path.Combine(PluginMain.PluginDirectory, "resources", "presets.json");
            var presetData = "{}";

            try
            {
                presetData = File.ReadAllText(presetFile);
            } catch(Exception ex)
            {
                Registry.Resolve<ILogger>().Log(LogLevel.Error, $"NewOverlayDialog: Failed to load presets: {ex}");
            }
            
            presets = JsonConvert.DeserializeObject<Dictionary<string, OverlayPreset>>(presetData);
            foreach (var pair in presets)
            {
                pair.Value.Name = pair.Key;
                cbPreset.Items.Add(pair.Value);
            }

            cbPreset.Items.Add(new OverlayPreset
            {
                Name = "Custom",
                Url = "special:custom",
            });

            cbPreset.DisplayMember = "Name";

            lblType.Visible = false;
            cbType.Visible = false;
            lblTypeDesc.Visible = false;

            textBox1.Focus();
        }
        
        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            preview?.Dispose();
            base.Dispose(disposing);
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            var preset = (OverlayPreset)cbPreset.SelectedItem;
            var name = textBox1.Text;

            if (NameValidator(name))
            {
                if (preset == null)
                {
                    MessageBox.Show("Please select a preset!");
                    DialogResult = DialogResult.None;
                    return;
                }

                if (preset.Url == "special:custom") 
                {
                    if (cbType.SelectedItem == null)
                    {
                        MessageBox.Show(Resources.PromptSelectOverlayType);
                        DialogResult = DialogResult.None;
                        return;
                    }

                    var overlayType = ((KeyValuePair<string, Type>)cbType.SelectedItem).Value;
                    var parameters = new NamedParameterOverloads();
                    parameters["config"] = null;
                    parameters["name"] = name;

                    SelectedOverlay = (IOverlay)Registry.Container.Resolve(overlayType, parameters);
                } else
                {
                    if (preview.GetType() == typeof(Overlays.MiniParseOverlay))
                    {
                        SelectedOverlay = new Overlays.MiniParseOverlay((Overlays.MiniParseOverlayConfig) preview.Config, name);
                    }
                    else
                    {
                        SelectedOverlay = preview;
                    }
                }

                DialogResult = DialogResult.OK;
            }
            else
            {
                DialogResult = DialogResult.None;
            }
        }

        private void cbPreset_SelectedIndexChanged(object sender, EventArgs e)
        {
            var preset = (OverlayPreset)cbPreset.SelectedItem;

            if (preset.Url == "special:custom")
            {
                lblType.Visible = true;
                cbType.Visible = true;
                lblTypeDesc.Visible = true;

                if (preview != null) preview.Visible = false;
            }
            else
            {
                lblType.Visible = false;
                cbType.Visible = false;
                lblTypeDesc.Visible = false;

                if (preview != null) preview.Dispose();

                switch (preset.Type)
                {
                    case "MiniParse":
                        var config = new Overlays.MiniParseOverlayConfig("Preview");
                        config.ActwsCompatibility = preset.Supports.Count == 1 && preset.Supports.Contains("actws");
                        config.Size = new Size(preset.Size[0], preset.Size[1]);
                        config.IsLocked = preset.Locked;

                        var overlay = new Overlays.MiniParseOverlay(config, "Preview");
                        overlay.Preview = true;
                        config.Url = preset.Url;

                        preview = overlay;
                        break;
                }
            }
        }

        private class ComboItem
        {
            public Type OverlayType { get; set; }
            public string FriendlyName { get; set; }

            public ComboItem(Type overlayType, string friendlyName)
            {
                OverlayType = overlayType;
                FriendlyName = friendlyName;
            }
        }

        [JsonObject(NamingStrategyType = typeof(Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy))]
        private class OverlayPreset
        {
            public string Name;
            public string Type;
            public string Url;
            [JsonIgnore]
            public int[] Size;
            public bool Locked;
            public List<string> Supports;

            [JsonExtensionData]
            private IDictionary<string, JToken> _others;

            [OnDeserialized]
            public void ParseOthers(StreamingContext ctx)
            {
                var size = _others["size"];
                Size = new int[2];

                for(int i = 0; i < 2; i++)
                {
                    switch (size[i].Type) {
                        case JTokenType.Integer:
                            Size[i] = size[i].ToObject<int>();
                            break;
                        case JTokenType.String:
                            var part = size[i].ToString();
                            if (part.EndsWith("%"))
                            {
                                var percent = float.Parse(part.Substring(0, part.Length - 1)) / 100;
                                var screenSize = Screen.PrimaryScreen.WorkingArea;

                                Size[i] = (int) Math.Round(percent * (i == 0 ? screenSize.Width : screenSize.Height));
                            } else
                            {
                                Size[i] = int.Parse(part);
                            }
                            break;
                        default:
                            Size[i] = 300;
                            break;
                    }
                }
            }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
