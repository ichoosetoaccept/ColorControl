﻿using LgTv;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ColorControl
{
    class LgDevice
    {
        public class InvokableAction
        {
            public Func<Dictionary<string, object>, bool> Function { get; set; }
            public string Name { get; set; }
            public Type EnumType { get; set; }
            public decimal MinValue { get; set; }
            public decimal MaxValue { get; set; }
            public string Category { get; set; }
            public string Title { get; set; }
            public int CurrentValue { get; set; }
            public LgPreset Preset { get; set; }
        }

        public class LgDevicePictureSettings
        {
            public int Backlight { get; set; }
            public int Contrast { get; set; }
            public int Brightness { get; set; }
            public int Color { get; set; }
        }

        public enum PowerState
        {
            Unknown,
            Active,
            Power_Off,
            Suspend,
            Active_Standby,
            Screen_Off
        }

        public enum PowerOffSource
        {
            Unknown,
            App,
            Manually
        }

        protected static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        public static Func<string, string[], bool> ExternalServiceHandler;
        public static List<string> DefaultActionsOnGameBar = new() { "backlight", "contrast", "brightness", "color" };

    public string Name { get; private set; }
        public string IpAddress { get; private set; }
        public string MacAddress { get; private set; }
        public bool IsCustom { get; private set; }
        [JsonIgnore]
        public bool IsDummy { get; private set; }

        public bool PowerOnAfterStartup { get; set; }
        public bool PowerOnAfterResume { get; set; }
        public bool PowerOffOnShutdown { get; set; }
        public bool PowerOffOnStandby { get; set; }
        public bool PowerSwitchOnScreenSaver { get; set; }

        private List<string> _actionsForGameBar;

        public List<string> ActionsOnGameBar { 
            get
            {
                return _actionsForGameBar;
            }
            set
            {
                if (value?.Any() == false)
                {
                    _actionsForGameBar = DefaultActionsOnGameBar;
                }
                else
                {
                    _actionsForGameBar = value;
                }
            } 
        }

        [JsonIgnore]
        public PowerState CurrentState { get; set; }

        [JsonIgnore]
        private LgTvApi _lgTvApi;
        [JsonIgnore]
        private bool _justWokeUp;
        [JsonIgnore]
        public PowerOffSource PoweredOffBy { get; private set; }

        [JsonIgnore]
        public bool PoweredOffViaApp { get; private set; }
        [JsonIgnore]
        private DateTimeOffset _poweredOffViaAppDateTime { get; set; }
        [JsonIgnore]
        private List<InvokableAction> _invokableActions = new List<InvokableAction>();
        [JsonIgnore]
        private SemaphoreSlim _connectSemaphore = new SemaphoreSlim(1, 1);

        [JsonIgnore]
        public string ModelName { get; private set; }

        [JsonIgnore]
        public LgDevicePictureSettings PictureSettings { get; private set; }

        public event EventHandler PictureSettingsChangedEvent;

        [JsonConstructor]
        public LgDevice(string name, string ipAddress, string macAddress, bool isCustom = true, bool isDummy = false)
        {
            PictureSettings = new LgDevicePictureSettings();
            ActionsOnGameBar = new List<string> { "backlight", "contrast", "brightness", "color" };

            Name = name;
            IpAddress = ipAddress;
            MacAddress = macAddress;
            IsCustom = isCustom;
            IsDummy = isDummy;

            AddInvokableAction("WOL", new Func<Dictionary<string, object>, bool>(WakeAction));
            AddGenericPictureAction("backlight", minValue: 0, maxValue: 100);
            AddGenericPictureAction("brightness", minValue: 0, maxValue: 100);
            AddGenericPictureAction("contrast", minValue: 0, maxValue: 100);
            AddGenericPictureAction("color", minValue: 0, maxValue: 100);
            AddGenericPictureAction("pictureMode", typeof(PictureMode), title: "Picture Mode");
            AddGenericPictureAction("colorGamut", typeof(ColorGamut), title: "Color Gamut");
            AddGenericPictureAction("dynamicContrast", typeof(OffToHigh), title: "Dynamic Contrast");
            AddGenericPictureAction("gamma", typeof(GammaExp));
            //AddGenericPictureAction("dynamicColor", typeof(OffToAuto));
            //AddGenericPictureAction("superResolution", typeof(OffToAuto));
            AddGenericPictureAction("peakBrightness", typeof(OffToHigh), title: "Peak Brightness");
            AddGenericPictureAction("smoothGradation", typeof(OffToAuto), title: "Smooth Gradation");
            AddGenericPictureAction("energySaving", typeof(EnergySaving), title: "Energy Saving");
            AddGenericPictureAction("hdrDynamicToneMapping", typeof(DynamicTonemapping), title: "HDR Dynamic Tone Mapping");
            //AddGenericPictureAction("blackLevel", typeof(LowToAuto));
            //AddGenericPictureAction("ambientLightCompensation", typeof(OffToAuto2));
            //AddGenericPictureAction("truMotionMode", typeof(TruMotionMode));
            AddGenericPictureAction("motionProOLED", typeof(OffToHigh), title: "OLED Motion Pro");
            AddGenericPictureAction("uhdDeepColorHDMI1", typeof(OffToOn), category: "other");
            AddGenericPictureAction("uhdDeepColorHDMI2", typeof(OffToOn), category: "other");
            AddGenericPictureAction("uhdDeepColorHDMI3", typeof(OffToOn), category: "other");
            AddGenericPictureAction("uhdDeepColorHDMI4", typeof(OffToOn), category: "other");
            AddGenericPictureAction("gameOptimizationHDMI1", typeof(OffToOn), category: "other");
            AddGenericPictureAction("gameOptimizationHDMI2", typeof(OffToOn), category: "other");
            AddGenericPictureAction("gameOptimizationHDMI3", typeof(OffToOn), category: "other");
            AddGenericPictureAction("gameOptimizationHDMI4", typeof(OffToOn), category: "other");
            //AddGenericPictureAction("freesyncOLEDHDMI4", typeof(OffToOn), category: "other");
            //AddGenericPictureAction("freesyncSupport", typeof(OffToOn), category: "other");
            AddGenericPictureAction("hdmiPcMode_hdmi1", typeof(FalseToTrue), category: "other");
            AddGenericPictureAction("hdmiPcMode_hdmi2", typeof(FalseToTrue), category: "other");
            AddGenericPictureAction("hdmiPcMode_hdmi3", typeof(FalseToTrue), category: "other");
            AddGenericPictureAction("hdmiPcMode_hdmi4", typeof(FalseToTrue), category: "other");
            AddGenericPictureAction("adjustingLuminance", minValue: -50, maxValue: 50);
            AddInvokableAction("turnScreenOff", new Func<Dictionary<string, object>, bool>(TurnScreenOffAction));
            AddInvokableAction("turnScreenOn", new Func<Dictionary<string, object>, bool>(TurnScreenOnAction));

            AddInternalPresetAction(new LgPreset("InStart", "com.webos.app.factorywin", new[] { "0", "4", "1", "3" }, new { id = "executeFactory", irKey = "inStart" }));
            AddInternalPresetAction(new LgPreset("EzAdjust", "com.webos.app.factorywin", new[] { "0", "4", "1", "3" }, new { id = "executeFactory", irKey = "ezAdjust" }));
            AddInternalPresetAction(new LgPreset("Software Update", "com.webos.app.softwareupdate", null, new { mode = "user", flagUpdate = true }));

            AddSetDeviceConfigAction("HDMI_1_icon", typeof(HdmiIcon), "HDMI 1 icon");
            AddSetDeviceConfigAction("HDMI_2_icon", typeof(HdmiIcon), "HDMI 2 icon");
            AddSetDeviceConfigAction("HDMI_3_icon", typeof(HdmiIcon), "HDMI 3 icon");
            AddSetDeviceConfigAction("HDMI_4_icon", typeof(HdmiIcon), "HDMI 4 icon");
        }

        private void AddInvokableAction(string name, Func<Dictionary<string, object>, bool> function)
        {
            var action = new InvokableAction
            {
                Name = name,
                Function = function
            };

            _invokableActions.Add(action);
        }

        private void AddInternalPresetAction(LgPreset preset)
        {
            var action = new InvokableAction
            {
                Name = preset.name,
                Preset = preset
            };

            _invokableActions.Add(action);
        }

        private void AddGenericPictureAction(string name, Type type = null, decimal minValue = 0, decimal maxValue = 0, string category = "picture", string title = null)
        {
            var action = new InvokableAction
            {
                Name = name,
                Function = new Func<Dictionary<string, object>, bool>(GenericPictureAction),
                EnumType = type,
                MinValue = minValue,
                MaxValue = maxValue,
                Category = category,
                Title = title == null ? Utils.FirstCharUpperCase(name) : title
            };

            _invokableActions.Add(action);
        }

        private void AddSetDeviceConfigAction(string name, Type type, string title)
        {
            var action = new InvokableAction
            {
                Name = name,
                Function = new Func<Dictionary<string, object>, bool>(GenericDeviceConfigAction),
                EnumType = type,
                Title = title == null ? Utils.FirstCharUpperCase(name) : title
            };

            _invokableActions.Add(action);
        }

        public void AddGameBarAction(string name)
        {
            if (!ActionsOnGameBar.Contains(name))
            {
                ActionsOnGameBar.Add(name);
            }
        }

        public void RemoveGameBarAction(string name)
        {
            ActionsOnGameBar.Remove(name);
        }

        public override string ToString()
        {
            //return (IsDummy ? string.Empty : (IsCustom ? "Custom: " : "Auto detect: ")) + $"{Name}" + (!string.IsNullOrEmpty(IpAddress) ? $" ({IpAddress})" : string.Empty);
            return $"{(IsDummy ? string.Empty : (IsCustom ? "Custom: " : "Auto detect: "))}{Name}{(!string.IsNullOrEmpty(IpAddress) ? ", " + IpAddress : string.Empty)}";
        }

        public async Task<bool> Connect(int retries = 3)
        {
            var locked = _connectSemaphore.CurrentCount == 0;
            await _connectSemaphore.WaitAsync();
            try
            {
                if (locked && _lgTvApi != null)
                {
                    return true;
                }

                try
                {
                    DisposeConnection();
                    _lgTvApi = await LgTvApi.CreateLgTvApi(IpAddress, retries);

                    //Test();
                    //_lgTvApi.Test3();
                    if (_lgTvApi != null)
                    {
                        var info = await _lgTvApi.GetSystemInfo("modelName");
                        if (info != null)
                        {
                            ModelName = info.modelName;
                        }

                        //await _lgTvApi.SubscribeVolume(VolumeChanged);
                        await _lgTvApi.SubscribePowerState(PowerStateChanged);
                        await _lgTvApi.SubscribePictureSettings(PictureSettingsChanged);

                        //await _lgTvApi.SetDeviceConfig("PC");

                        //var result = await GetPictureSettings();

                        //await _lgTvApi.SetSystemSettings("adjustingLuminance", new[] { 0, 0, -5, -10, -15, -20, -25, -30, -35, -40, -45, -50, -50, -50, -40, -30, -20, -10, 10, 20, 30, 50 });
                        //await _lgTvApi.SetSystemSettings("adjustingLuminance", new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

                        //await _lgTvApi.SetInput("HDMI_1");
                        //await Task.Delay(2000);
                        //await _lgTvApi.SetConfig("com.palm.app.settings.enableHdmiPcLabel", true);
                        //await _lgTvApi.SetInput("HDMI_2");
                    }
                    return _lgTvApi != null;
                }
                catch (Exception ex)
                {
                    string logMessage = ex.ToLogString(Environment.StackTrace);
                    Logger.Error($"Error while connecting to {IpAddress}: {logMessage}");
                    return false;
                }
            }
            finally
            {
                _connectSemaphore.Release();
            }
        }

        public bool VolumeChanged(dynamic payload)
        {
            return true;
        }

        public bool PictureSettingsChanged(dynamic payload)
        {
            if (payload.settings != null)
            {
                var settings = payload.settings;

                Logger.Debug($"PictureSettingsChanged: {JsonConvert.SerializeObject(settings)}");

                if (settings.backlight != null)
                {
                    PictureSettings.Backlight = Utils.ParseDynamicAsInt(settings.backlight, PictureSettings.Backlight);
                }
                if (settings.contrast != null)
                {
                    PictureSettings.Contrast = Utils.ParseDynamicAsInt(settings.contrast, PictureSettings.Contrast);
                }
                if (settings.brightness != null)
                {
                    PictureSettings.Brightness = Utils.ParseDynamicAsInt(settings.brightness, PictureSettings.Brightness);
                }
                if (settings.color != null)
                {
                    PictureSettings.Color = Utils.ParseDynamicAsInt(settings.color, PictureSettings.Color);
                }
            }

            PictureSettingsChangedEvent?.Invoke(this, EventArgs.Empty);

            return true;
        }

        public bool PowerStateChanged(dynamic payload)
        {
            Logger.Debug($"[{Name}] Power state change: {JsonConvert.SerializeObject(payload)}");

            var state = ((string)payload.state).Replace(' ', '_');

            PowerState newState;
            if (Enum.TryParse(state, out newState))
            {
                CurrentState = newState;

                if (CurrentState == PowerState.Active)
                {
                    if (payload.processing == null && (DateTimeOffset.Now - _poweredOffViaAppDateTime).TotalMilliseconds > 500)
                    {
                        PoweredOffViaApp = false;
                        PoweredOffBy = PowerOffSource.Unknown;
                        _poweredOffViaAppDateTime = DateTimeOffset.MinValue;
                    }
                }
                else {
                    PoweredOffBy = PoweredOffViaApp ? PowerOffSource.App : PowerOffSource.Manually;
                }
            }
            else
            {
                CurrentState = PowerState.Unknown;
                Logger.Warn($"Unknown power state: {state}");
            }

            Logger.Debug($"PoweredOffBy: {PoweredOffBy}, PoweredOffViaApp: {PoweredOffViaApp}");

            return true;
        }

        public bool IsConnected()
        {
            return !_lgTvApi?.ConnectionClosed ?? false;
        }

        internal void DisposeConnection()
        {
            if (_lgTvApi != null)
            {
                _lgTvApi.Dispose();
                _lgTvApi = null;
            }
        }

        public async Task<bool> ExecutePreset(LgPreset preset, bool reconnect, LgServiceConfig config)
        {
            var hasApp = !string.IsNullOrEmpty(preset.appId);

            var hasWOL = preset.steps.Any(s => s.Equals("WOL", StringComparison.OrdinalIgnoreCase));

            if (hasWOL)
            {
                var connected = await WakeAndConnect(0);
                if (!connected)
                {
                    return false;
                }
            }

            for (var tries = 0; tries <= 1; tries++)
            {
                if (!await Connected(reconnect || tries == 1))
                {
                    return false;
                }

                if (hasApp)
                {
                    try
                    {
                        var @params = preset.AppParams;
                        if (@params == null && config.ShowAdvancedActions)
                        {
                            if (preset.appId.Equals("com.webos.app.softwareupdate"))
                            {
                                @params = new { mode = "user", flagUpdate = true };
                            }
                            else if (preset.appId.Equals("com.webos.app.factorywin"))
                            {
                                if (preset.name.Contains("ezadjust", StringComparison.OrdinalIgnoreCase))
                                {
                                    @params = new { id = "executeFactory", irKey = "ezAdjust" };
                                }
                                else
                                {
                                    @params = new { id = "executeFactory", irKey = "inStart" };
                                }
                            }
                        }

                        await _lgTvApi.LaunchApp(preset.appId, @params);
                    }
                    catch (Exception ex)
                    {
                        string logMessage = ex.ToLogString(Environment.StackTrace);
                        Logger.Error("Error while launching app: " + logMessage);

                        if (tries == 0)
                        {
                            continue;
                        }
                        return false;
                    }

                    if (_justWokeUp)
                    {
                        _justWokeUp = false;
                        await Task.Delay(1000);
                    }
                }

                if (preset.steps.Any())
                {
                    if (hasApp)
                    {
                        await Task.Delay(1500);
                    }
                    try
                    {
                        await ExecuteSteps(_lgTvApi, preset);
                    }
                    catch (Exception ex)
                    {
                        string logMessage = ex.ToLogString(Environment.StackTrace);
                        Logger.Error("Error while executing steps: " + logMessage);

                        if (tries == 0)
                        {
                            continue;
                        }
                        return false;
                    }
                }

                return true;
            }

            return true;
        }

        private async Task ExecuteSteps(LgTvApi api, LgPreset preset)
        {
            var mouse = await api.GetMouse();
            foreach (var step in preset.steps)
            {
                var keySpec = step.Split(':');
                var key = keySpec[0].ToUpper();
                var index = key.IndexOf("(");
                string[] parameters = null;
                if (index > -1)
                {
                    var keyValue = keySpec[0].Split('(');
                    key = keyValue[0];
                    parameters = keyValue[1].Substring(0, keyValue[1].Length - 1).Split(';');
                }

                var delay = 0;
                if (keySpec.Length == 2)
                {
                    delay = Utils.ParseInt(keySpec[1]);
                }

                var executeKey = true;
                var action = _invokableActions.FirstOrDefault(a => a.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (action != null)
                {
                    ExecuteAction(action, parameters);

                    executeKey = false;
                }
                if (ExternalServiceHandler != null && parameters != null)
                {
                    if (ExternalServiceHandler(key, parameters))
                    {
                        executeKey = false;
                    }
                }

                if (executeKey)
                {
                    SendKey(mouse, key);
                    delay = delay == 0 ? 180 : delay;
                }

                if (delay > 0)
                {
                    await Task.Delay(delay);
                }
            }
        }

        public void ExecuteAction(InvokableAction action, string[] parameters)
        {
            var function = action.Function;
            if (function == null)
            {
                return;
            }

            if (parameters?.Length > 0)
            {
                var keyValues = new Dictionary<string, object> {
                    { "name", action.Name },
                    { "value", parameters },
                    { "category", action.Category }
                };

                function(keyValues);
                return;
            }

            function(null);
        }

        private void SendKey(LgWebOsMouseService mouse, string key)
        {
            if (key.Length >= 1 && int.TryParse(key[0].ToString(), out _))
            {
                key = "_" + key;
            }
            var button = (ButtonType)Enum.Parse(typeof(ButtonType), key);
            mouse.SendButton(button);
        }

        public async Task<LgWebOsMouseService> GetMouseAsync()
        {
            return await _lgTvApi.GetMouse();
        }

        public async Task<IEnumerable<LgApp>> GetApps(bool force = false)
        {
            if (!await Connected(force))
            {
                Logger.Debug("Cannot refresh apps: no connection could be made");
                return new List<LgApp>();
            }

            return await _lgTvApi.GetApps(force);
        }

        internal async Task<bool> PowerOff()
        {
            if (!await Connected(true) || CurrentState != PowerState.Active)
            {
                return false;
            }

            PoweredOffViaApp = true;
            _poweredOffViaAppDateTime = DateTimeOffset.Now;

            await _lgTvApi.TurnOff();

            return true;
        }

        internal async Task<bool> TestConnection(int retries = 1)
        {
            if (!await Connected(true, retries))
            {
                return false;
            }

            try
            {
                await _lgTvApi.IsMuted();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("TestConnection: " + ex.ToLogString());
                return false;
            }
        }

        internal async Task<bool> WakeAndConnect(int wakeDelay = 5000, int connectDelay = 500)
        {
            try
            {
                //if (wakeDelay == 0)
                //{
                //    if (await ConnectToSelectedDevice())
                //    {
                //        Logger.Debug("Already connected, no wake needed?");
                //        return;
                //    };
                //}

                await Task.Delay(wakeDelay);
                var result = Wake();
                if (!result)
                {
                    Logger.Debug("WOL failed");
                    return false;
                }
                Logger.Debug("WOL succeeded");
                await Task.Delay(connectDelay);
                result = await Connect();
                Logger.Debug("Connect succeeded: " + result);
                return result;
            }
            catch (Exception e)
            {
                Logger.Error("WakeAndConnectToSelectedDevice: " + e.ToLogString());
                return false;
            }
        }

        internal bool Wake()
        {
            var result = false;

            if (MacAddress != null)
            {
                result = WOL.WakeFunction(MacAddress);
                _justWokeUp = true;
            }
            else
            {
                Logger.Debug("Cannot wake device: the device has no MAC-address");
            }

            return result;
        }

        internal async Task<bool> WakeAndConnectWithRetries(int retries = 5)
        {
            var wakeDelay = 0;
            var maxRetries = retries <= 1 ? 5 : retries;

            var result = false;
            for (var retry = 0; retry < maxRetries && !result; retry++)
            {
                Logger.Debug($"WakeAndConnectWithRetries: attempt {retry + 1} of {maxRetries}...");

                var ms = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                result = await WakeAndConnect(retry == 0 ? wakeDelay : 0);
                ms = DateTimeOffset.Now.ToUnixTimeMilliseconds() - ms;
                if (!result)
                {
                    var delay = 2000 - ms;
                    if (delay > 0)
                    {
                        await Task.Delay((int)delay);
                    }
                }
            }

            if (result)
            {
                var resumeScript = Path.Combine(Program.DataDir, "ResumeScript.bat");
                if (File.Exists(resumeScript))
                {
                    Utils.StartProcess(resumeScript);
                }
            }

            return result;
        }

        public List<InvokableAction> GetInvokableActions()
        {
            return _invokableActions;
        }

        public List<InvokableAction> GetInvokableActionsForGameBar()
        {
            var actions = GetInvokableActions();
            return actions.Where(a => a.Category == "picture" && (a.EnumType != null && a.EnumType != typeof(PictureMode) || a.MinValue >= 0 && a.MaxValue > 0)).ToList();
        }

        public List<InvokableAction> GetActionsForGameBar()
        {
            var actions = GetInvokableActions();
            return actions.Where(a => ActionsOnGameBar.Contains(a.Name)).ToList();
        }

        public async Task PowerOn()
        {
            var mouse = await _lgTvApi.GetMouse();
            mouse.SendButton(ButtonType.POWER);
        }

        public void Test()
        {
            try
            {
                //_lgTvApi.GetServiceList();
                //_lgTvApi?.Test();
            }
            catch (Exception ex)
            {
                Logger.Error("TEST: " + ex.ToLogString());
            }
        }

        private async Task<bool> Connected(bool reconnect = false, int retries = 3)
        {
            if (reconnect || !IsConnected() || !string.Equals(_lgTvApi.GetIpAddress(), IpAddress))
            {
                if (!await Connect(retries))
                {
                    Logger.Debug("Cannot apply LG-preset: no connection could be made");
                    return false;
                }
            }
            return true;
        }

        internal void ConvertToCustom()
        {
            IsCustom = true;
        }

        public bool IsUsingHDRPictureMode()
        {
            // Temporary workaround because I cannot read the picture mode at this time
            return PictureSettings.Backlight == 100 && PictureSettings.Contrast == 100;
        }

        internal async Task SetBacklight(int backlight)
        {
            await _lgTvApi.SetSystemSettings("backlight", backlight.ToString());
        }

        internal async Task SetContrast(int contrast)
        {
            await _lgTvApi.SetSystemSettings("contrast", contrast.ToString());
        }

        public async Task SetOLEDMotionPro(string mode)
        {
            await _lgTvApi.SetConfig("tv.model.motionProMode", mode);
        }

        internal async Task SetConfig(string key, object value)
        {
            await _lgTvApi.SetConfig(key, value);
        }
        internal async Task SetSystemSettings(string name, string value)
        {
            await _lgTvApi.SetSystemSettings(name, value);

            UpdateCurrentValueOfAction(name, value);
        }

        internal async Task<dynamic> GetPictureSettings()
        {
            //var keys = new[] { "backlight", "brightness", "contrast", "color", "pictureMode", "colorGamut", "dynamicContrast", "peakBrightness", "smoothGradation", "energySaving", "motionProOLED" };
            var keys = new[] { "backlight", "brightness", "contrast", "color" };

            return await _lgTvApi.GetSystemSettings("picture", keys);
        }

        private bool WakeAction(Dictionary<string, object> parameters)
        {
            return Wake();
        }

        private bool TurnScreenOffAction(Dictionary<string, object> parameters)
        {
            var task = _lgTvApi.TurnScreenOff();
            Utils.WaitForTask(task);

            return true;
        }

        private bool TurnScreenOnAction(Dictionary<string, object> parameters)
        {
            var task = _lgTvApi.TurnScreenOn();
            Utils.WaitForTask(task);

            return true;
        }

        private bool GenericPictureAction(Dictionary<string, object> parameters)
        {
            var settingName = parameters["name"].ToString();
            var stringValues = parameters["value"] as string[];
            var category = parameters["category"].ToString();
            object value = stringValues[0];
            if (stringValues.Length > 1)
            {
                value = stringValues.Select(s => int.Parse(s)).ToArray();
            }
            var task = _lgTvApi.SetSystemSettings(settingName, value, category);
            Utils.WaitForTask(task);

            UpdateCurrentValueOfAction(settingName, value.ToString());

            return true;
        }

        private bool GenericDeviceConfigAction(Dictionary<string, object> parameters)
        {
            var id = parameters["name"].ToString().Replace("_icon", string.Empty);
            var stringValues = parameters["value"] as string[];
            var value = stringValues[0];

            var description = Utils.GetDescriptionByEnumName<HdmiIcon>(value);

            var task = _lgTvApi.SetDeviceConfig(id, value, description);
            Utils.WaitForTask(task);

            return true;
        }

        private void UpdateCurrentValueOfAction(string settingName, string value)
        {
            if (settingName != "backlight" && settingName != "contrast" && settingName != "brightness" && settingName != "color")
            {
                var action = _invokableActions.FirstOrDefault(a => a.Name == settingName);
                if (action != null)
                {
                    if (action.EnumType != null)
                    {
                        try
                        {
                            var enumValue = Enum.Parse(action.EnumType, value);
                            var intEnum = (int)enumValue;
                            action.CurrentValue = intEnum;
                        }
                        catch (Exception) { }
                    }
                    else
                    {
                        action.CurrentValue = 0;
                    }
                }
            }
        }
    }
}
