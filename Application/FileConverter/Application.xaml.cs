﻿// <copyright file="Application.xaml.cs" company="AAllard">License: http://www.gnu.org/licenses/gpl.html GPL version 3.</copyright>

/*  File Converter - This program allow you to convert file format to another.
    Copyright (C) 2017 Adrien Allard
    email: adrien.allard.pro@gmail.com

    This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or any later version.

    This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

namespace FileConverter
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Windows;

    using FileConverter.ConversionJobs;
    using FileConverter.Services;
    using FileConverter.Views;

    using GalaSoft.MvvmLight.Ioc;

    using Debug = FileConverter.Diagnostics.Debug;

    public partial class Application : System.Windows.Application
    {
        private static readonly Version Version = new Version()
                                                      {
                                                          Major = 1,
                                                          Minor = 2,
                                                          Patch = 3,
                                                      };

        private bool needToRunConversionThread;
        private bool cancelAutoExit;
        private bool isSessionEnding;
        private bool verbose;
        private bool showSettings;
        private bool showHelp;
        
        public event EventHandler<ApplicationTerminateArgs> OnApplicationTerminate;

        public static Version ApplicationVersion => Application.Version;

        public void CancelAutoExit()
        {
            this.cancelAutoExit = true;

            if (this.OnApplicationTerminate != null)
            {
                this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(float.NaN));
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.RegisterServices();

            this.Initialize();

            // Navigate to the wanted view.
            INavigationService navigationService = SimpleIoc.Default.GetInstance<INavigationService>();

            if (this.showHelp)
            {
                navigationService.NavigateTo(Pages.Help);
                return;
            }

            if (this.needToRunConversionThread)
            {
                navigationService.NavigateTo(Pages.Main);

                IConversionService conversionService = SimpleIoc.Default.GetInstance<IConversionService>();
                conversionService.ConversionJobsTerminated += this.ConversionService_ConversionJobsTerminated;
                conversionService.ConvertFilesAsync();
            }

            if (this.showSettings)
            {
                navigationService.NavigateTo(Pages.Settings);
            }

            if (this.verbose)
            {
                navigationService.NavigateTo(Pages.Diagnostics);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            Debug.Log("Exit application.");

            IUpgradeService upgradeService = SimpleIoc.Default.GetInstance<IUpgradeService>();

            if (!this.isSessionEnding && upgradeService.UpgradeVersionDescription != null && upgradeService.UpgradeVersionDescription.NeedToUpgrade)
            {
                Debug.Log("A new version of file converter has been found: {0}.", upgradeService.UpgradeVersionDescription.LatestVersion);

                if (string.IsNullOrEmpty(upgradeService.UpgradeVersionDescription.InstallerPath))
                {
                    Debug.LogError("Invalid installer path.");
                }
                else
                {
                    Debug.Log("Wait for the end of the installer download.");
                    while (upgradeService.UpgradeVersionDescription.InstallerDownloadInProgress)
                    {
                        Thread.Sleep(1000);
                    }

                    string installerPath = upgradeService.UpgradeVersionDescription.InstallerPath;
                    if (!System.IO.File.Exists(installerPath))
                    {
                        Debug.LogError("Can't find upgrade installer ({0}). Try to restart the application.", installerPath);
                        return;
                    }

                    // Start process.
                    Debug.Log("Start file converter upgrade from version {0} to {1}.", ApplicationVersion, upgradeService.UpgradeVersionDescription.LatestVersion);

                    ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true, };

                    Debug.Log("Start upgrade process: {0}{1}.", System.IO.Path.GetFileName(startInfo.FileName), startInfo.Arguments);
                    Process process = new System.Diagnostics.Process { StartInfo = startInfo };

                    process.Start();
                }
            }

            Debug.Release();
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            base.OnSessionEnding(e);

            this.isSessionEnding = true;
            this.Shutdown();
        }

        private void RegisterServices()
        {
            if (this.TryFindResource("Settings") == null)
            {
                Debug.LogError("Can't retrieve conversion service.");
                this.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
            }

            if (this.TryFindResource("Locator") == null)
            {
                Debug.LogError("Can't retrieve view model locator.");
                this.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
            }

            if (this.TryFindResource("Conversions") == null)
            {
                Debug.LogError("Can't retrieve conversion service.");
                this.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
            }

            if (this.TryFindResource("Navigation") == null)
            {
                Debug.LogError("Can't retrieve navigation service.");
                this.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
            }

            if (this.TryFindResource("Upgrade") == null)
            {
                Debug.LogError("Can't retrieve navigation service.");
                this.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
            }

            INavigationService navigationService = SimpleIoc.Default.GetInstance<INavigationService>();

            navigationService.RegisterPage<HelpWindow>(Pages.Help, false);
            navigationService.RegisterPage<MainWindow>(Pages.Main, false);
            navigationService.RegisterPage<SettingsWindow>(Pages.Settings, true);
            navigationService.RegisterPage<DiagnosticsWindow>(Pages.Diagnostics, true);
            navigationService.RegisterPage<UpgradeWindow>(Pages.Upgrade, true);
        }

        private void Initialize()
        {
#if BUILD32
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + " (32 bits)");
#else
            Diagnostics.Debug.Log("File Converter v" + ApplicationVersion.ToString() + " (64 bits)");
#endif

            // Retrieve arguments.
            Debug.Log("Retrieve arguments...");
            string[] args = Environment.GetCommandLineArgs();

#if (DEBUG)
            {
                System.Array.Resize(ref args, 3);
                args[1] = "--settings";
                args[2] = "--verbose";
            }

#endif

            // Log arguments.
            for (int index = 0; index < args.Length; index++)
            {
                string argument = args[index];
                Debug.Log("Arg{0}: {1}", index, argument);
            }

            Debug.Log(string.Empty);

            if (args.Length == 1)
            {
                // Display help windows to explain that this application is a context menu extension.
                this.showHelp = true;
                return;
            }

            // Parse arguments.
            List<string> filePaths = new List<string>();
            string conversionPresetName = null;
            for (int index = 1; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (argument.StartsWith("--"))
                {
                    // This is an optional parameter.
                    string parameterTitle = argument.Substring(2).ToLowerInvariant();

                    switch (parameterTitle)
                    {
                        case "post-install-init":
                            Settings.PostInstallationInitialization();
                            Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                            return;

                        case "version":
                            Console.Write(ApplicationVersion.ToString());
                            Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                            return;

                        case "settings":
                            this.showSettings = true;
                            break;

                        case "apply-settings":
                            Settings.ApplyTemporarySettings();
                            Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                            return;

                        case "conversion-preset":
                            if (index >= args.Length - 1)
                            {
                                Debug.LogError("Invalid format. (code 0x01)");
                                Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                                return;
                            }

                            conversionPresetName = args[index + 1];
                            index++;
                            continue;

                        case "verbose":
                            {
                                this.verbose = true;
                            }

                            break;

                        default:
                            Debug.LogError("Unknown application argument: '--{0}'.", parameterTitle);
                            return;
                    }
                }
                else
                {
                    filePaths.Add(argument);
                }
            }

            ISettingsService settingsService = SimpleIoc.Default.GetInstance<ISettingsService>();
            if (settingsService.Settings == null)
            {
                Diagnostics.Debug.LogError("The application will now shutdown. If you want to fix the problem yourself please edit or delete the file: C:\\Users\\UserName\\AppData\\Local\\FileConverter\\Settings.user.xml.");
                Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                return;
            }
            
            // Check for upgrade.
            if (settingsService.Settings.CheckUpgradeAtStartup)
            {
                IUpgradeService upgradeService = SimpleIoc.Default.GetInstance<IUpgradeService>();
                upgradeService.NewVersionAvailable += this.UpgradeService_NewVersionAvailable;
                upgradeService.CheckForUpgrade();
            }

            ConversionPreset conversionPreset = null;
            if (!string.IsNullOrEmpty(conversionPresetName))
            {
                conversionPreset = settingsService.Settings.GetPresetFromName(conversionPresetName);
                if (conversionPreset == null)
                {
                    Debug.LogError("Invalid conversion preset '{0}'. (code 0x02)", conversionPresetName);
                    Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
                    return;
                }
            }

            if (conversionPreset != null)
            {
                IConversionService conversionService = SimpleIoc.Default.GetInstance<IConversionService>();

                // Create conversion jobs.
                Debug.Log("Create jobs for conversion preset: '{0}'", conversionPreset.Name);
                try
                {
                    for (int index = 0; index < filePaths.Count; index++)
                    {
                        string inputFilePath = filePaths[index];
                        ConversionJob conversionJob = ConversionJobFactory.Create(conversionPreset, inputFilePath);

                        conversionService.RegisterConversionJob(conversionJob);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError(exception.Message);
                    throw;
                }

                this.needToRunConversionThread = true;
            }
        }

        private void UpgradeService_NewVersionAvailable(object sender, UpgradeVersionDescription e)
        {
            SimpleIoc.Default.GetInstance<INavigationService>().NavigateTo(Pages.Upgrade);

            IUpgradeService upgradeService = SimpleIoc.Default.GetInstance<IUpgradeService>();
            upgradeService.NewVersionAvailable -= this.UpgradeService_NewVersionAvailable;
        }

        private void ConversionService_ConversionJobsTerminated(object sender, ConversionJobsTerminatedEventArgs e)
        {
            IConversionService conversionService = SimpleIoc.Default.GetInstance<IConversionService>();
            conversionService.ConversionJobsTerminated -= this.ConversionService_ConversionJobsTerminated;

            ISettingsService settingsService = SimpleIoc.Default.GetInstance<ISettingsService>();

            if (!settingsService.Settings.ExitApplicationWhenConversionsFinished)
            {
                return;
            }
            
            if (this.cancelAutoExit)
            {
                return;
            }

            if (e.AllConversionsSucceed)
            {
                float remainingTime = settingsService.Settings.DurationBetweenEndOfConversionsAndApplicationExit;
                while (remainingTime > 0f)
                {
                    if (this.OnApplicationTerminate != null)
                    {
                        this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                    }

                    Thread.Sleep(1000);
                    remainingTime--;

                    if (this.cancelAutoExit)
                    {
                        return;
                    }
                }

                if (this.OnApplicationTerminate != null)
                {
                    this.OnApplicationTerminate.Invoke(this, new ApplicationTerminateArgs(remainingTime));
                }

                this.Dispatcher.BeginInvoke((Action)(() => Application.Current.Shutdown()));
            }
        }
    }
}
