﻿using ControllerCommon;
using ControllerCommon.Managers;
using ControllerCommon.Utils;
using Force.Crc32;
using HandheldCompanion.Managers.Classes;
using HandheldCompanion.Views;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.Json;
using static ControllerCommon.Utils.ProcessUtils;

namespace HandheldCompanion.Managers
{
    public class ProfileManager : Manager
    {
        private Dictionary<bool, uint> CRCs = new Dictionary<bool, uint>()
        {
            { false, 0xcd4906cc },
            { true, 0x1e9df650 },
        };

        public Dictionary<string, Profile> profiles = new Dictionary<string, Profile>(StringComparer.InvariantCultureIgnoreCase);
        public FileSystemWatcher profileWatcher { get; set; }

        #region events
        public event DeletedEventHandler Deleted;
        public delegate void DeletedEventHandler(Profile profile);
        public event UpdatedEventHandler Updated;
        public delegate void UpdatedEventHandler(Profile profile, bool backgroundtask, bool isCurrent);
        public event LoadedEventHandler Ready;
        public delegate void LoadedEventHandler();

        public event AppliedEventHandler Applied;
        public delegate void AppliedEventHandler(Profile profile);

        public event DiscardedEventHandler Discarded;
        public delegate void DiscardedEventHandler(Profile profile, bool isCurrent);
        #endregion

        public Profile currentProfile = new();

        public ProfileManager() : base()
        {
            // initialiaze path
            Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HandheldCompanion", "profiles");
            if (!Directory.Exists(Path))
                Directory.CreateDirectory(Path);

            MainWindow.processManager.ForegroundChanged += ProcessManager_ForegroundChanged;
            MainWindow.processManager.ProcessStarted += ProcessManager_ProcessStarted;
            MainWindow.processManager.ProcessStopped += ProcessManager_ProcessStopped;
        }

        public override void Start()
        {
            // initialize profile files
            ResourceSet resourceSet = Properties.Resources.ResourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);
            foreach (DictionaryEntry entry in resourceSet)
            {
                string resourceKey = entry.Key.ToString();
                if (!resourceKey.Contains(".json"))
                    continue;

                object resource = entry.Value;
                string profile_path = System.IO.Path.Combine(Path, resourceKey);

                if (!File.Exists(profile_path))
                    File.WriteAllText(profile_path, (string)resource);
            }

            // monitor profile file deletions
            profileWatcher = new FileSystemWatcher()
            {
                Path = Path,
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                Filter = "*.json",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size
            };
            profileWatcher.Deleted += ProfileDeleted;

            // process existing profiles
            string[] fileEntries = Directory.GetFiles(Path, "*.json", SearchOption.AllDirectories);
            foreach (string fileName in fileEntries)
                ProcessProfile(fileName);

            // warn owner
            Ready?.Invoke();

            base.Start();
        }

        public override void Stop()
        {
            if (!IsInitialized)
                return;

            profileWatcher.Deleted -= ProfileDeleted;
            profileWatcher.Dispose();

            base.Stop();
        }

        public bool Contains(Profile profile)
        {
            foreach (Profile pr in profiles.Values)
                if (pr.executable == profile.executable)
                    return true;

            return false;
        }

        public int GetProfileIndex(Profile profile)
        {
            int idx = -1;

            for (int i = 0; i < profiles.Count; i++)
            {
                Profile pr = profiles.Values.ToList()[i];
                if (pr.executable == profile.executable)
                    return i;
            }

            return idx;
        }

        private void ProcessManager_ProcessStopped(ProcessEx processEx)
        {
            try
            {
                Profile profile = GetProfileFromExec(processEx.Name);

                if (profile == null)
                    return;

                if (profile.isRunning)
                {
                    profile.isRunning = false;

                    // warn owner
                    bool isCurrent = profile.executable == currentProfile.executable;

                    // (re)set current profile
                    if (isCurrent)
                        currentProfile = GetDefault();

                    // raise event
                    Discarded?.Invoke(profile, isCurrent);

                    // update profile
                    UpdateOrCreateProfile(profile, true, false);
                }
            }
            catch (Exception) { }
        }

        private void ProcessManager_ProcessStarted(ProcessEx processEx, bool startup)
        {
            try
            {
                Profile profile = GetProfileFromExec(processEx.Name);

                if (profile == null)
                    return;

                profile.fullpath = processEx.Path;
                profile.isRunning = true;

                // update profile
                UpdateOrCreateProfile(profile, true, false);
            }
            catch (Exception) { }
        }

        private void ProcessManager_ForegroundChanged(ProcessEx processEx, ProcessEx backgroundEx)
        {
            try
            {
                var profile = GetProfileFromExec(processEx.Name);

                if (profile == null)
                    profile = GetDefault();

                if (!profile.isEnabled)
                    return;

                // skip if is current profile
                if (currentProfile == profile)
                    return;

                // raise event
                Discarded?.Invoke(currentProfile, true);
                Applied?.Invoke(profile);

                // update current profile
                currentProfile = profile;

                LogManager.LogDebug("Profile {0} applied", profile.name);

                if (profile.isDefault)
                {
                    // inform service
                    MainWindow.pipeClient.SendMessage(new PipeClientProfile { profile = profile, backgroundTask = true });

                    // do not update default profile path
                    return;
                }

                // send toast
                // todo: localize me
                MainWindow.toastManager.SendToast($"Profile {profile.name} applied");

                profile.isRunning = true;
                profile.fullpath = processEx.Path;
                UpdateOrCreateProfile(profile, true, false);
            }
            catch (Exception) { }
        }

        private void ProfileDeleted(object sender, FileSystemEventArgs e)
        {
            string ProfileName = e.Name.Replace(".json", "");

            if (!profiles.ContainsKey(ProfileName))
                return;

            Profile profile = profiles[ProfileName];

            switch (ProfileName)
            {
                case "Default":
                    SerializeProfile(profile);
                    break;
                default:
                    DeleteProfile(profile);
                    break;
            }
        }

        public Profile GetDefault()
        {
            if (profiles.ContainsKey("Default"))
                return profiles["Default"];
            return new();
        }

        private void ProcessProfile(string fileName)
        {
            Profile profile = null;
            try
            {
                string outputraw = File.ReadAllText(fileName);
                profile = JsonSerializer.Deserialize<Profile>(outputraw);
                profile.fullpath = profile.path;
            }
            catch (Exception ex)
            {
                LogManager.LogError("Could not parse profile {0}. {1}", fileName, ex.Message);
            }

            // failed to parse
            if (profile == null || profile.name == null || profile.path == null)
            {
                LogManager.LogError("Could not parse profile {0}.", fileName);
                return;
            }

            if (profile.name == "Default")
            {
                profile.isDefault = true;

                // set current profile
                currentProfile = profile;
            }

            UpdateOrCreateProfile(profile);
        }

        public void DeleteProfile(Profile profile)
        {
            string settingsPath = System.IO.Path.Combine(Path, profile.json);

            if (profiles.ContainsKey(profile.name))
            {
                UnregisterApplication(profile);
                profiles.Remove(profile.name);

                // warn owner
                bool isCurrent = profile.executable == currentProfile.executable;

                // (re)set current profile
                if (isCurrent)
                    currentProfile = GetDefault();

                // raise event(s)
                Deleted?.Invoke(profile);
                Discarded?.Invoke(profile, isCurrent);

                // send toast
                // todo: localize me
                MainWindow.toastManager.SendToast($"Profile {profile.name} deleted");

                LogManager.LogInformation("Deleted profile {0}", settingsPath);
            }

            File.Delete(settingsPath);
        }

        public void SerializeProfile(Profile profile)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(profile, options);

            string settingsPath = System.IO.Path.Combine(Path, profile.json);
            File.WriteAllText(settingsPath, jsonString);
        }

        private ProfileErrorCode SanitizeProfile(Profile profile)
        {
            string processpath = System.IO.Path.GetDirectoryName(profile.fullpath);

            if (profile.isDefault)
                return ProfileErrorCode.IsDefault;
            else
            {
                if (!Directory.Exists(processpath))
                    return ProfileErrorCode.MissingPath;
                else if (!File.Exists(profile.fullpath))
                    return ProfileErrorCode.MissingExecutable;
                else if (!CommonUtils.IsDirectoryWritable(processpath))
                    return ProfileErrorCode.MissingPermission;
            }

            return ProfileErrorCode.None;
        }

        public void UpdateOrCreateProfile(Profile profile, bool backgroundtask = true, bool fullUpdate = true)
        {
            // refresh error code
            profile.error = SanitizeProfile(profile);
            profile.json = $"{System.IO.Path.GetFileNameWithoutExtension(profile.executable)}.json";

            // update database
            profiles[profile.name] = profile;

            // warn owner
            bool isCurrent = profile.executable == currentProfile.executable;

            // raise event(s)
            Updated?.Invoke(profile, backgroundtask, isCurrent);

            // inform service
            if (isCurrent)
                MainWindow.pipeClient.SendMessage(new PipeClientProfile { profile = currentProfile, backgroundTask = backgroundtask });

            if (profile.error != ProfileErrorCode.None && !profile.isDefault)
            {
                LogManager.LogError("Profile {0} returned error code {1}", profile.name, profile.error);
                return;
            }

            // only bother updating wrapper and cloaking on profile creation or process start
            if (fullUpdate)
            {
                // update wrapper
                UpdateProfileWrapper(profile);

                // update cloaking
                UpdateProfileCloaking(profile);
            }
        }

        public void UpdateProfileCloaking(Profile profile)
        {
            if (profile.error == ProfileErrorCode.MissingExecutable || profile.error == ProfileErrorCode.MissingPath)
                return;

            if (profile.whitelisted)
                RegisterApplication(profile);
            else
                UnregisterApplication(profile);
        }

        public void UpdateProfileWrapper(Profile profile)
        {
            // deploy xinput wrapper
            string XinputPlus = Properties.Resources.XInputPlus;

            string[] fullpaths = new string[] { profile.fullpath };

            // for testing purposes, this should not happen!
            if (profile.isDefault)
            {
                fullpaths = new string[]
                {
                    @"C:\Windows\System32\cmd.exe",
                    @"C:\Windows\SysWOW64\cmd.exe"
                };
            }

            foreach (string fullpath in fullpaths)
            {
                string processpath = System.IO.Path.GetDirectoryName(fullpath);
                string inipath = System.IO.Path.Combine(processpath, "XInputPlus.ini");
                bool iniexist = File.Exists(inipath);

                // get binary type (x64, x86)
                BinaryType bt; GetBinaryType(fullpath, out bt);
                bool x64 = bt == BinaryType.SCS_64BIT_BINARY;

                if (profile.use_wrapper)
                    File.WriteAllText(inipath, XinputPlus);
                else if (iniexist)
                    File.Delete(inipath);

                for (int i = 0; i < 5; i++)
                {
                    string dllpath = System.IO.Path.Combine(processpath, $"xinput1_{i + 1}.dll");
                    string backpath = System.IO.Path.Combine(processpath, $"xinput1_{i + 1}.back");

                    // dll has a different naming format
                    if (i == 4)
                    {
                        dllpath = System.IO.Path.Combine(processpath, $"xinput9_1_0.dll");
                        backpath = System.IO.Path.Combine(processpath, $"xinput9_1_0.back");
                    }

                    bool dllexist = File.Exists(dllpath);
                    bool backexist = File.Exists(backpath);

                    byte[] data = new byte[] { 0 };

                    // check CRC32
                    if (dllexist) data = File.ReadAllBytes(dllpath);
                    var crc = Crc32Algorithm.Compute(data);
                    bool is_x360ce = CRCs[x64] == crc;

                    // pull data from dll
                    data = x64 ? Properties.Resources.xinput1_x64 : Properties.Resources.xinput1_x86;

                    // do not try to write/erase files when profile is used
                    if (profile.isRunning)
                        return;

                    switch (profile.error)
                    {
                        // do not try to write/erase files when access is denied
                        case ProfileErrorCode.MissingPermission:
                        case ProfileErrorCode.MissingPath:
                            return;
                    }

                    if (profile.use_wrapper)
                    {
                        if (dllexist && is_x360ce)
                            continue; // skip to next file
                        else if (!dllexist)
                            File.WriteAllBytes(dllpath, data);
                        else if (dllexist && !is_x360ce)
                        {
                            // create backup of current dll
                            if (!backexist)
                                File.Move(dllpath, backpath);

                            // deploy wrapper
                            File.WriteAllBytes(dllpath, data);
                        }
                    }
                    else
                    {
                        // delete wrapper dll
                        if (dllexist && is_x360ce)
                            File.Delete(dllpath);

                        // restore backup is exists
                        if (backexist)
                            File.Move(backpath, dllpath);
                    }
                }
            }
        }

        public Profile GetProfileFromExec(string processExec)
        {
            foreach (Profile pr in profiles.Values)
                if (pr.executable.Equals(processExec, StringComparison.InvariantCultureIgnoreCase))
                    return pr;
            return null;
        }

        public void UnregisterApplication(Profile profile)
        {
            MainWindow.pipeClient?.SendMessage(new PipeClientHidder
            {
                action = HidderAction.Unregister,
                path = profile.fullpath
            });
        }

        public void RegisterApplication(Profile profile)
        {
            MainWindow.pipeClient?.SendMessage(new PipeClientHidder
            {
                action = HidderAction.Register,
                path = profile.fullpath
            });
        }
    }
}
