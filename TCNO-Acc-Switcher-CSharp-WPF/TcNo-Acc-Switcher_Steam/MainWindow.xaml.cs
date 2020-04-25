﻿// For registry keys
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
//using System.Windows.Shapes; -- Commented because of clash with System.IO.Path. If causes issues, uncomment.
using System.Xml;

namespace TcNo_Acc_Switcher_Steam
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        List<Steamuser> userAccounts = new List<Steamuser>();
        List<string> fLoginUsersLines = new List<string>();
        MainWindowViewModel MainViewmodel = new MainWindowViewModel();

        //int version = 1;
        readonly int version = 2302;
        readonly int trayversion = 1000;
        readonly Color DarkGreen = Color.FromRgb(5, 51, 5);
        readonly Color DefaultGray = Color.FromRgb(51, 51, 51);


        // Settings will load later. Just defined here.
        UserSettings persistentSettings = new UserSettings();
        readonly SolidColorBrush _vacRedBrush = new SolidColorBrush(Color.FromRgb(255,41,58));

        public MainWindow()
        {
            /* TODO:
             - Make "Installer" .exe. Maybe from C++ so it can run everywhere? Check for the correct .NET Core Desktop version, and download it if not found. Then run the installer.
             Download the .exe and place it where the user specifies.
             Start the Account Swicther with an argument that automatically makes the Start Menu and or Desktop Shortcuts, if specified.
             Change it so that the correct Tray application is extracted by default on first launch, instead of whenever the shortcut button is clicked. Of course, check for it then as well to make sure it's copied.
             -- Get the .NET Core version from PC, and compare with a version file on https://tcno.co, that also has the latest download links for users.
             */
            // Single instance check
            if (SelfAlreadyRunning())
            {
                Console.WriteLine(Strings.SwitcherAlreadyRunning);
                MessageBox.Show(Strings.SwitcherAlreadyRunning, Strings.SwitcherAlreadyRunningHeading, MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(99);
            }

            // Crash handler
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);


            singleFileUpdateClean(); // Clean extra files from before the 2.2.1 update (When the program was made single file)
            if (Directory.Exists("Resources"))
            {
                resourceClean(true);
                if (File.Exists("RestartTray"))
                {
                    File.Delete("RestartTray");
                    startTray();
                }
                // Because closing a messagebox before the window shows causes it to crash for some reason...
                MessageBoxResult messageBoxResult = MessageBox.Show(Strings.GitHubWhatsNew, Strings.FinishedUpdating, System.Windows.MessageBoxButton.YesNo);
                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("https://github.com/TcNobo/TcNo-Acc-Switcher/releases") { UseShellExecute = true });
                }
            }
            else
            {
                if (File.Exists("UpdateFound.txt"))
                    downloadUpdateDialog();
                else
                {
                    Thread updateCheckThread = new Thread(updateCheck);
                    updateCheckThread.Start();
                }
            }
            MainViewmodel.ProgramVersion = Strings.Version + ": " + version.ToString();

            if (File.Exists("DeleteImagesOnStart"))
            {
                File.Delete("DeleteImagesOnStart");
                new DirectoryInfo("Images").Delete(true);
            }

            this.DataContext = MainViewmodel;
            InitializeSettings(); // Load user settings
            InitializeComponent();
            updateFromSettings(); // Update components
            CheckShortcuts();

            // Create image folder
            Directory.CreateDirectory("images");
            RefreshSteamAccounts();
        }
        private static bool SelfAlreadyRunning()
        {
            Process[] processes = Process.GetProcesses();
            Process currentProc = Process.GetCurrentProcess();
            foreach (Process process in processes)
            {
                if (currentProc.ProcessName == process.ProcessName && currentProc.Id != process.Id)
                {
                    return true;
                }
            }
            return false;
        }


        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Log Unhandled Exception
            string exceptionStr = e.ExceptionObject.ToString();
            using (StreamWriter sw = File.AppendText("AccSwitcher-Crashlog.txt"))
            {
                sw.WriteLine(DateTime.Now.ToString() + "\t" + Strings.ErrUnhandledCrash + ": " + exceptionStr + Environment.NewLine + Environment.NewLine);
            }
            MessageBox.Show(Strings.ErrUnhandledException, Strings.ErrUnhandledExceptionHeader, MessageBoxButton.OK, MessageBoxImage.Error);
            MessageBox.Show(Strings.ErrSubmitCrashlog, Strings.ErrUnhandledExceptionHeader, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        public void RefreshSteamAccounts()
        {
            // Collect Steam Account basic info from Steam file
            lblStatus.Content = Strings.StatusCollectingAccounts;
            checkBrokenImages();
            getSteamAccounts();

            // Clear incase it's a refresh
            MainViewmodel.SteamUsers.Clear();
            listAccounts.Items.Refresh();

            // Check if profile images exist, otherwise queue
            List<Steamuser> ImagesToDownload = new List<Steamuser>();
            foreach (Steamuser su in userAccounts)
            {
                if (!File.Exists(su.ImgURL))
                {
                    ImagesToDownload.Add(su);
                }
                else
                {
                    su.ImgURL = Path.GetFullPath(su.ImgURL);
                    MainViewmodel.SteamUsers.Add(su);
                }
            }

            if (File.Exists("SteamVACCache.json") && persistentSettings.ShowVACStatus)
                loadVacInformation();

            if (ImagesToDownload.Count > 0)
            {
                Thread t = new Thread(new ParameterizedThreadStart(DownloadImages));
                t.Start(ImagesToDownload);
                lblStatus.Content = Strings.StatusImageDownloadStart;
            }
            else
            {
                lblStatus.Content = Strings.StatusReady;
            }
        }
        private void extract7zip()
        {
            if (!Directory.Exists("Resources"))
                Directory.CreateDirectory("Resources");
            File.WriteAllBytes(Path.Combine("Resources", "7za.exe"), Properties.Resources._7za);
            File.WriteAllText(Path.Combine("Resources", "7za-license.txt"), Properties.Resources.License);
        }
        void downloadUpdateDialog()
        {
            MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show(Strings.UpdateNow, Strings.UpdateFound, System.Windows.MessageBoxButton.YesNo);
            if (messageBoxResult == MessageBoxResult.Yes)
            {
                if (File.Exists("UpdateFound.txt"))
                    File.Delete("UpdateFound.txt");

                // Extract embedded files
                extract7zip();

                string zPath = Path.Combine("Resources", "7za.exe"),
                        updzip = "upd.7z",
                        ePath = Directory.GetCurrentDirectory();
#if X64
                string arch = "x64";
                File.WriteAllBytes(updzip, Properties.Resources.update64);
#else
                string arch = "x32";
                File.WriteAllBytes(updzip, Properties.Resources.update32);
#endif

                ProcessStartInfo pro = new ProcessStartInfo();
                pro.WindowStyle = ProcessWindowStyle.Hidden;
                pro.FileName = zPath;
                pro.Arguments = string.Format("x \"{0}\" -y -o\"{1}\"", updzip, ePath);
                pro.UseShellExecute = false;
                pro.RedirectStandardOutput = true;
                pro.CreateNoWindow = true;
                Process x = Process.Start(pro);
                x.WaitForExit();

                File.Delete("UpdateInformation.txt");
                using (FileStream fs = File.Create("UpdateInformation.txt"))
                {
                    byte[] info = new UTF8Encoding(true).GetBytes(System.AppDomain.CurrentDomain.FriendlyName + "|" + arch + "|" + version.ToString());
                    fs.Write(info, 0, info.Length);
                }

                // Close tray application
                var proc = Process.GetProcessesByName("TcNo Account Switcher Tray").FirstOrDefault();
                if (proc != null)
                {
                    File.Create("RestartTray");
                    closeTray();
                }

                // Run update.exe
                string processName = "TcNo-Acc-Switcher-Updater.exe";
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = processName;
                startInfo.CreateNoWindow = false;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
                Environment.Exit(1);
            }
        }
        private void startTray()
        {
            try
            {
                string processName = "TcNo Account Switcher Tray.exe";
                if (File.Exists(processName))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = Path.GetFullPath(processName);
                    startInfo.CreateNoWindow = false;
                    startInfo.UseShellExecute = false;
                    Process.Start(startInfo);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(Strings.ErrTrayProcessStart, Strings.ErrTrayProcessStartHead, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void closeTray()
        {
            // This is what Administrator permissions are required for.
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C TASKKILL /F /T /IM \"TcNo Account Switcher Tray*\"";
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
        }
        void updateCheck()
        {
            try
            {
                System.Net.WebClient wc = new System.Net.WebClient();
                int webVersion = int.Parse(wc.DownloadString("https://tcno.co/Projects/AccSwitcher/version.php").Substring(0, 4));

                if (webVersion > version)
                {
                    using (FileStream fs = File.Create("UpdateFound.txt"))
                    {
                        byte[] info = new UTF8Encoding(true).GetBytes(Strings.UpdateLastLaunch + DateTime.Now.ToString());
                        fs.Write(info, 0, info.Length);
                    }
                    this.Dispatcher.Invoke(() =>
                    {
                        downloadUpdateDialog();
                    });
                }
                else
                {
                    if (File.Exists("UpdateFound.txt"))
                        File.Delete("UpdateFound.txt");
                }
            }
            catch (WebException ex)
            {
                MessageBox.Show(Strings.UpdateLastLaunch + ex.ToString(), Strings.ErrUpdateCheckFail, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        bool vacBanned = false;
        void DownloadImages(object oin)
        {
            List<Steamuser> ImagesToDownload = (List<Steamuser>)oin;

            int totalUsers = ImagesToDownload.Count();
            int currentUser = 0;
            bool downloadError = false;
            // DISABLE LISTBOX
            foreach (Steamuser su in ImagesToDownload)
            {
                currentUser++;
                this.Dispatcher.Invoke(() =>
                {
                    lblStatus.Content = $"{Strings.StatusDownloadingProfile} {currentUser.ToString()}/{totalUsers}";
                });
                string imageURL = getUserImageURL(su.SteamID);

                if (!string.IsNullOrEmpty(imageURL))
                {
                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.DownloadFile(new Uri(imageURL), su.ImgURL);
                        }
                    }
                    catch (WebException ex)
                    {
                        if (!downloadError)
                        {
                            downloadError = true; // Show error only once
                            // .net Core way: File.WriteAllBytes(su.ImgURL, Properties.Resources.QuestionMark); // Give the user's profile picture a question mark.
                            Properties.Resources.QuestionMark.Save(su.ImgURL);
                            MessageBox.Show($"{Strings.ErrImageDownloadFail} {ex.ToString()}", Strings.ErrProfileImageDlFail, MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                else
                {
                    // .net Core way: File.WriteAllBytes(su.ImgURL, Properties.Resources.QuestionMark);
                    Properties.Resources.QuestionMark.Save(su.ImgURL);
                }
                su.ImgURL = Path.GetFullPath(su.ImgURL);
                if (persistentSettings.ShowVACStatus)
                    su.vacStatus = vacBanned ? _vacRedBrush : Brushes.Transparent;

                this.Dispatcher.Invoke(() =>
                {
                    MainViewmodel.SteamUsers.Add(su);
                });
            }
            this.Dispatcher.Invoke(() =>
            {
                saveVacInformation();
                lblStatus.Content = Strings.StatusReady;
            });
            // ENABLE LISTBOX
        }
        public void Window_SizeUpdated(object sender, RoutedEventArgs e)
        {
            //if (resultsTab.IsSelected)
            //{
            //    Grid.SetRowSpan(dataGrid1, 2);
            //    Grid.SetRowSpan(dataGrid2, 2);
            //}
        }
        void InitializeSettings()
        {
            if (!File.Exists("SteamSettings.json"))
                saveSettings();
            else
                loadSettings();
            bool validSteamFound = (File.Exists(persistentSettings.SteamEXE()));
            //bool validSteamFound = false; // Testing
            if (!validSteamFound)
            {
                validSteamFound = setAndCheckSteamFolder(false);
                if (!validSteamFound)
                {
                    MessageBox.Show(Strings.RequiredPickSteamDir);
                    Environment.Exit(1);
                    // this.Close() won't work, because the main window hasn't appeared just yet. Still needs to be populated with Steam Accounts.
                }
            }
        }
        bool setAndCheckSteamFolder(bool manual)
        {
            if (!manual)
            {
                MainViewmodel.SteamNotFound = true;
                string ProgramFiles = "C:\\Program Files\\Steam\\Steam.exe",
                       ProgramFiles86 = "C:\\Program Files (x86)\\Steam\\Steam.exe";
                bool exists = File.Exists(ProgramFiles),
                     exists86 = File.Exists(ProgramFiles86);

                if (exists86)
                    persistentSettings.SteamFolder = Directory.GetParent(ProgramFiles86).ToString();
                else if (exists)
                    persistentSettings.SteamFolder = Directory.GetParent(ProgramFiles).ToString();

                if (exists86 || exists)
                {
                    saveSettings();
                    return (File.Exists(persistentSettings.SteamEXE()));
                }
            }
            else
            {
                MainViewmodel.SteamNotFound = false;
            }

            SteamFolderInput getInputFolderDialog = new SteamFolderInput();
            getInputFolderDialog.DataContext = MainViewmodel;
            getInputFolderDialog.ShowDialog();
            if (!String.IsNullOrEmpty(MainViewmodel.InputFolderDialogResponse))
            {
                persistentSettings.SteamFolder = MainViewmodel.InputFolderDialogResponse;
                saveSettings();
                return (File.Exists(persistentSettings.SteamEXE()));
            }
            else
                return false;
        }
        void loadSettings()
        {
            JsonSerializer serializer = new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore };
            using (StreamReader sr = new StreamReader(@"SteamSettings.json"))
            {
                // persistentSettings = JsonConvert.DeserializeObject<UserSettings>(sr.ReadToEnd()); -- Entirely replaces, instead of merging. New variables won't have values.
                // Using a JSON Union Merge means that settings that are missing will have default values, set at the top of this file.
                JObject jCurrent = JObject.Parse(JsonConvert.SerializeObject(persistentSettings));
                try
                {
                    jCurrent.Merge(JObject.Parse(sr.ReadToEnd()), new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Union
                    });
                    persistentSettings = jCurrent.ToObject<UserSettings>();
                }
                catch (Exception)
                {
                    if (File.Exists("SteamSettings.json"))
                    {
                        if (File.Exists("SteamSettings.old.json"))
                            File.Delete("SteamSettings.old.json");
                        File.Copy("SteamSettings.json", "SteamSettings.old.json");
                    }

                    saveSettings();
                    MessageBox.Show(Strings.ErrSteamSettingsLoadFail, Strings.ErrSteamSettingsLoadFailHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        void updateFromSettings()
        {
            MainViewmodel.StartAsAdmin = persistentSettings.StartAsAdmin;
            MainViewmodel.ShowSteamID = persistentSettings.ShowSteamID;
            MainViewmodel.ShowVACStatus = persistentSettings.ShowVACStatus;
            MainViewmodel.InputFolderDialogResponse = persistentSettings.SteamFolder;
            MainViewmodel.ForgetAccountEnabled = persistentSettings.ForgetAccountEnabled;
            this.Width = persistentSettings.WindowSize.Width;
            this.Height = persistentSettings.WindowSize.Height;
            ShowSteamIDHidden.IsChecked = persistentSettings.ShowSteamID;
            toggleVACStatus(persistentSettings.ShowVACStatus);
        }
        void saveOtherVarsToSettings()
        {
            persistentSettings.WindowSize = new Size(this.Width, this.Height);
            persistentSettings.StartAsAdmin = MainViewmodel.StartAsAdmin;
            persistentSettings.ShowVACStatus = MainViewmodel.ShowVACStatus;
            persistentSettings.SteamFolder = MainViewmodel.InputFolderDialogResponse;
            persistentSettings.ForgetAccountEnabled = MainViewmodel.ForgetAccountEnabled;
        }
        void saveSettings()
        {
            if (!Double.IsNaN(this.Height))
            {
                // Verifies that the program has started properly. Can be any property to do with the window. Just using Width.
                saveOtherVarsToSettings();
                JsonSerializer serializer = new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore };

                using (StreamWriter sw = new StreamWriter(@"SteamSettings.json"))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, persistentSettings);
                }
            }
        }
        public static string UnixTimeStampToDateTime(string unixTimeStampString)
        {
            double unixTimeStamp = Convert.ToDouble(unixTimeStampString);
            // Unix timestamp is seconds past epoch
            var localDateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(long.Parse(unixTimeStampString)).DateTime.ToLocalTime();
            return localDateTimeOffset.ToString("dd/MM/yyyy hh:mm:ss");
        }
        public bool IsValidGDIPlusImage(string filename)
        {
            //From https://stackoverflow.com/questions/8846654/read-image-and-determine-if-its-corrupt-c-sharp
            try
            {
                using (var bmp = new System.Drawing.Bitmap(filename))
                {
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private void checkBrokenImages()
        {
            if (Directory.Exists("images"))
            {
                DirectoryInfo d = new DirectoryInfo("images");
                foreach (var file in d.GetFiles("*.jpg"))
                {
                    try
                    {
                        if (!IsValidGDIPlusImage(file.FullName)) // Delete image if is not as valid, working image.
                        {
                            File.Delete(file.FullName);
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            File.Delete(file.FullName);
                        }
                        catch (Exception)
                        {
                            MessageBox.Show($"{Strings.ErrEmptyImage} {ex.ToString()}", Strings.ErrEmptyImageHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                            throw;
                        }
                    }
                }
            }
        }
        void getSteamAccounts()
        {
            string line, lineNoQuot;
            string username = "", steamID = "", rememberAccount = "", personaName = "", timestamp = "";

            // Clear incase it's a refresh
            fLoginUsersLines.Clear();
            userAccounts.Clear();

            try
            {
                System.IO.StreamReader file = new System.IO.StreamReader(persistentSettings.LoginusersVDF());

                while ((line = file.ReadLine()) != null)
                {
                    fLoginUsersLines.Add(line);
                    line = line.Replace("\t", "");
                    lineNoQuot = line.Replace("\"", "");

                    if (lineNoQuot.All(char.IsDigit) && string.IsNullOrEmpty(steamID)) // Line is SteamID and steamID is empty >> New user.
                    {
                        steamID = lineNoQuot;
                    }
                    else if (lineNoQuot.All(char.IsDigit) && !string.IsNullOrEmpty(steamID)) // If steamID isn't empty, save account details, empty temp vars for collection.
                    {
                        userAccounts.Add(new Steamuser() { Name = personaName, AccName = username, SteamID = steamID, ImgURL = Path.Combine("images", $"{steamID}.jpg"), lastLogin = UnixTimeStampToDateTime(timestamp) });
                        username = "";
                        rememberAccount = "";
                        personaName = "";
                        timestamp = "";
                        steamID = lineNoQuot;
                    }
                    else if (line.Contains("AccountName"))
                    {
                        username = lineNoQuot.Substring(11, lineNoQuot.Length - 11);
                    }
                    else if (line.Contains("RememberPassword"))
                    {
                        rememberAccount = lineNoQuot.Substring(lineNoQuot.Length - 1);
                    }
                    else if (line.Contains("PersonaName"))
                    {
                        personaName = lineNoQuot.Substring(11, lineNoQuot.Length - 11);
                    }
                    else if (line.Contains("Timestamp"))
                    {
                        timestamp = lineNoQuot.Substring(9, lineNoQuot.Length - 9);
                    }

                    System.Console.WriteLine(line);
                }
                // While loop adds account when new one started. Will not include the last one, so that's done here.
                userAccounts.Add(new Steamuser() { Name = personaName, AccName = username, SteamID = steamID, ImgURL = Path.Combine("images", $"{steamID}.jpg"), lastLogin = UnixTimeStampToDateTime(timestamp) });

                file.Close();
            }
            catch (System.IO.FileNotFoundException ex)
            {
                MessageBox.Show(Strings.ErrLoginusersNonExist, Strings.ErrLoginusersNonExistHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                MessageBox.Show($"{Strings.ErrInformation} {ex.ToString()}", Strings.ErrLoginusersNonExistHeader, MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(2);
            }
        }
        string getUserImageURL(string steamID)
        {
            string imageURL = "";
            XmlDocument profileXML = new XmlDocument();
            profileXML.Load($"https://steamcommunity.com/profiles/{steamID}?xml=1");
            imageURL = "";
            if (profileXML.DocumentElement.SelectNodes("/profile/privacyMessage").Count == 0) // Fix for accounts that haven't set up their Community Profile
            {
                try
                {
                    imageURL = profileXML.DocumentElement.SelectNodes("/profile/avatarFull")[0].InnerText;
                    bool isVAC = profileXML.DocumentElement.SelectNodes("/profile/vacBanned")[0].InnerText == "1" ? true : false;
                    bool isLimited = profileXML.DocumentElement.SelectNodes("/profile/isLimitedAccount")[0].InnerText == "1" ? true : false;
                    vacBanned = isVAC || isLimited;
                }
                catch (NullReferenceException) // User has not set up their account, or does not have an image.
                {
                    imageURL = "";
                }
            }
            return imageURL;
        }
        private void SteamUserSelect(object sender, SelectionChangedEventArgs e)
        {
            if (!listAccounts.IsLoaded) return;
            var item = (ListBox)sender;
            var su = (Steamuser)item.SelectedItem;
            try
            {
                lblStatus.Content = $"{Strings.StatusAccountSelected} {su.Name}";
                HeaderInstruction.Content = Strings.StatusPressLogin;
                btnLogin.IsEnabled = true;
                btnLogin.Background = new SolidColorBrush(MainViewmodel.SelectedSteamUser != null ? DarkGreen : DefaultGray);
            }
            catch
            {
                // Non-existent user account is selected, or none are available.
            }
        }
        //private void SteamUserUnselect(object sender, RoutedEventArgs e)
        //{
        //    if (!listAccounts.IsLoaded) return;
        //    //MessageBox.Show("You have selected a ListBoxItem!");
        //    MessageBox.Show(MainViewmodel.SelectedSteamUser.Name);
        //}
        private void UpdateLoginusers(bool loginnone)
        {
            // -----------------------------------
            // ----- Manage "loginusers.vdf" -----
            // -----------------------------------
            Byte[] info;
            using (FileStream fs = File.Open(persistentSettings.LoginusersVDF(), FileMode.Truncate, FileAccess.Write, FileShare.None))
            {
                lblStatus.Content = Strings.StatusEditingLoginusers;
                string lineNoQuot;
                bool userIDMatch = false;
                string outline = "", SelectedSteamID = (loginnone ? "" : MainViewmodel.SelectedSteamUser.SteamID);
                foreach (string curline in fLoginUsersLines)
                {
                    outline = curline;

                    lineNoQuot = curline;
                    lineNoQuot = lineNoQuot.Replace("\t", "").Replace("\"", "");

                    if (lineNoQuot.All(char.IsDigit)) // Check if line is JUST digits -> SteamID
                    {
                        userIDMatch = false;
                        if (lineNoQuot == SelectedSteamID)
                        {
                            // Most recent ID matches! Set this account to active.
                            userIDMatch = true;
                        }
                    }
                    else if (curline.Contains("mostrecent"))
                    {
                        // Set every mostrecent to 0, unless it's the one you want to switch to.
                        if (!loginnone && userIDMatch)
                        {
                            outline = "\t\t\"mostrecent\"\t\t\"1\"";
                        }
                        else
                        {
                            outline = "\t\t\"mostrecent\"\t\t\"0\"";
                        }
                    }
                    info = new UTF8Encoding(true).GetBytes(outline + "\n");
                    fs.Write(info, 0, info.Length);
                }
            }

            // -----------------------------------
            // --------- Manage registry ---------
            // -----------------------------------
            /*
            ------------ Structure ------------
            HKEY_CURRENT_USER\Software\Valve\Steam\
                --> AutoLoginUser = username
                --> RememberPassword = 1
            */
            lblStatus.Content = Strings.StatusEditingRegistry;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam"))
            {
                if (loginnone)
                {
                    key.SetValue("AutoLoginUser", "");
                    key.SetValue("RememberPassword", 1);
                }
                else
                {
                    key.SetValue("AutoLoginUser", MainViewmodel.SelectedSteamUser.AccName);
                    key.SetValue("RememberPassword", 1);
                }
            }
        }
        public void closeSteam()
        {
            // This is what Administrator permissions are required for.
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C TASKKILL /F /T /IM steam*";
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
        }

        public class Steamuser
        {
            public string Name { get; set; }
            public string SteamID { get; set; }
            public string ImgURL { get; set; }
            public string lastLogin { get; set; }
            public string AccName { get; set; }
            public System.Windows.Media.Brush vacStatus { get; set; }
        }
        public class UserSettings
        {
            //UserSettings defaultSettings = new UserSettings { StartAsAdmin = false, SteamFolder = "C:\\Program Files (x86)\\Steam\\", ShowSteamID = false, WindowSize = new Size(773, 420) };
            public bool StartAsAdmin { get; set; } = false;
            public bool ShowSteamID { get; set; } = false;
            public bool ShowVACStatus { get; set; } = true;
            public bool ForgetAccountEnabled { get; set; } = false;
            public string SteamFolder { get; set; } = "C:\\Program Files (x86)\\Steam\\";
            public Size WindowSize { get; set; } = new Size(773, 420);
            public string LoginusersVDF()
            {
                return Path.Combine(SteamFolder, "config\\loginusers.vdf");
            }
            public string SteamEXE()
            {
                return Path.Combine(SteamFolder, "Steam.exe");
            }
        }

        public class MainWindowViewModel : INotifyPropertyChanged
        {
            public MainWindowViewModel()
            {
                SteamUsers = new ObservableCollection<Steamuser>();
                InputFolderDialogResponse = "";
                SteamNotFound = new bool();
                StartAsAdmin = new bool();
                ShowSteamID = new bool();
                ShowVACStatus = new bool();
                StartMenuIcon = new bool();
                StartWithWindows = new bool();
                DesktopShortcut = new bool();
                vacStatus = Brushes.Black;
                ProgramVersion = "";
                ForgetAccountEnabled = new bool();
            }

            public ObservableCollection<Steamuser> SteamUsers { get; private set; }

            private Steamuser _SelectedSteamUser;
            public Steamuser SelectedSteamUser
            {
                get { return _SelectedSteamUser; }
                set
                {
                    _SelectedSteamUser = value;
                }
            }
            private string _InputFolderDialogResponse;
            public string InputFolderDialogResponse
            {
                get { return _InputFolderDialogResponse; }
                set
                {
                    _InputFolderDialogResponse = value;
                }
            }
            private bool _ShowSteamID;
            public bool ShowSteamID
            {
                get
                {
                    return _ShowSteamID;
                }
                set
                {
                    _ShowSteamID = value;
                }
            }
            private bool _ShowVACStatus;
            public bool ShowVACStatus
            {
                get
                {
                    return _ShowVACStatus;
                }
                set
                {
                    _ShowVACStatus = value;
                }
            }
            private bool _StartMenuIcon;
            public bool StartMenuIcon
            {
                get
                {
                    return _StartMenuIcon;
                }
                set
                {
                    _StartMenuIcon = value;
                }
            }
            private bool _DesktopShortcut;
            public bool DesktopShortcut
            {
                get
                {
                    return _DesktopShortcut;
                }
                set
                {
                    _DesktopShortcut = value;
                }
            }
            private bool _StartWithWindows;
            public bool StartWithWindows
            {
                get
                {
                    return _StartWithWindows;
                }
                set
                {
                    _StartWithWindows = value;
                }
            }
            private bool _StartAsAdmin;
            public bool StartAsAdmin
            {
                get
                {
                    return _StartAsAdmin;
                }
                set
                {
                    _StartAsAdmin = value;
                }
            }
            private bool _SteamNotFound;
            public bool SteamNotFound
            {
                get
                {
                    return _SteamNotFound;
                }
                set
                {
                    _SteamNotFound = value;
                }
            }
            private bool _ForgetAccountEnabled;
            public bool ForgetAccountEnabled
            {
                get
                {
                    return _ForgetAccountEnabled;
                }
                set
                {
                    _ForgetAccountEnabled = value;
                }
            }
            private string _ProgramVersion;
            public string ProgramVersion
            {
                get
                {
                    return _ProgramVersion;
                }
                set
                {
                    _ProgramVersion = value;
                }
            }
            private System.Windows.Media.Brush _vacStatus;
            public System.Windows.Media.Brush vacStatus
            {
                get
                {
                    return _vacStatus;
                }
                set
                {
                    _vacStatus = value;
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected void NotifyPropertyChanged(String info)
            {
                if (PropertyChanged != null)
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(info));
                }
            }
        }

        private void LoginMouseDown(object sender, MouseButtonEventArgs e)
        {
            LoginSelected();
        }
        private void ListBoxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            LoginSelected();
        }
        private void LoginSelected()
        {
            saveSettings();
            LoginButtonAnimation(Color.FromRgb(12,12,12), DefaultGray, 2000);

            lblStatus.Content = "Logging into: " + MainViewmodel.SelectedSteamUser.Name;
            btnLogin.IsEnabled = false;

            MainViewmodel.SelectedSteamUser.lastLogin = DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss");
            listAccounts.Items.Refresh();

            lblStatus.Content = Strings.StatusClosingSteam;
            closeSteam();
            UpdateLoginusers(false);

            if (persistentSettings.StartAsAdmin)
                Process.Start(persistentSettings.SteamEXE());
            else
                Process.Start(new ProcessStartInfo("explorer.exe", persistentSettings.SteamEXE()));
            lblStatus.Content = Strings.StatusStartedSteam;
            btnLogin.IsEnabled = true;
        }
        private void ShowForgetRememberDialog()
        {
            ForgetAccountCheck ForgetAccountCheckDialog = new ForgetAccountCheck();
            ForgetAccountCheckDialog.ShareMainWindow(this);
            ForgetAccountCheckDialog.DataContext = MainViewmodel;
            ForgetAccountCheckDialog.Owner = this;
            ForgetAccountCheckDialog.ShowDialog();
        }
        public string GetForgottenBackupPath() { return Path.Combine(persistentSettings.SteamFolder, $"config\\TcNo-Acc-Switcher-Backups\\"); }
        public string GetPersistentFolder() { return Path.Combine(persistentSettings.SteamFolder, "config\\"); }
        public string GetSteamDirectory() { return persistentSettings.SteamFolder; }

        public void RestoreForgotten()
        {

        }
        public void ClearForgottenBackups()
        {
            if (MessageBox.Show(Strings.ClearBackups, Strings.AreYouSure, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                string BackupPath = GetForgottenBackupPath();
                try
                {
                    if (Directory.Exists(BackupPath))
                        Directory.Delete(BackupPath, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{Strings.ErrRecursivelyDelete} {ex.ToString()}", Strings.ErrDeleteFilesHeader);
                }
            }
        }
        public void OpenSteamFolder()
        {
            Process.Start(persistentSettings.SteamFolder);
        }
        private void DeleteSelected()
        {
            btnLogin.IsEnabled = false;

            // Check if user understands what "forget" does.
            if (!MainViewmodel.ForgetAccountEnabled)
            {
                ShowForgetRememberDialog();
                return;
            }

            // Backup loginusers.vdf
            string backupFileName = $"loginusers-{ DateTime.Now.ToString("dd-MM-yyyy_HH-mm-ss.fff")}.vdf",
             backupdir = Path.Combine(persistentSettings.SteamFolder, $"config\\TcNo-Acc-Switcher-Backups\\");
            if (!Directory.Exists(backupdir))
                Directory.CreateDirectory(backupdir);
            try
            {
                File.Copy(persistentSettings.LoginusersVDF(), Path.Combine(persistentSettings.SteamFolder, $"config\\TcNo-Acc-Switcher-Backups\\{backupFileName}"));
            }
            catch (IOException e) when (e.HResult == -2147024816) // File already exists -- User deleting > 1 account per second
            {
                File.Copy(persistentSettings.LoginusersVDF(), Path.Combine(persistentSettings.SteamFolder, $"config\\TcNo-Acc-Switcher-Backups\\{backupFileName}"));
            }

            // ---------------------------------------------
            // ----- Remove user from "loginusers.vdf" -----
            // ---------------------------------------------
            Byte[] info;
            using (FileStream fs = File.Open(persistentSettings.LoginusersVDF(), FileMode.Truncate, FileAccess.Write, FileShare.None))
            {
                lblStatus.Content = $"{Strings.StatusRemoving} {MainViewmodel.SelectedSteamUser.Name} {Strings.StatusFromLoginusers}";
                string lineNoQuot;
                bool userIDMatch = false,
                     completedRemove = false;
                string outline = "", SelectedSteamID = MainViewmodel.SelectedSteamUser.SteamID;
                List<string> newfLoginUsersLines = new List<string>();
                foreach (string curline in fLoginUsersLines)
                {
                    outline = curline;

                    lineNoQuot = curline;
                    lineNoQuot = lineNoQuot.Replace("\t", "").Replace("\"", "");
                    if (!completedRemove)
                    {
                        if (!userIDMatch)
                        {
                            if (lineNoQuot.All(char.IsDigit)) // Check if line is JUST digits -> SteamID
                            {
                                userIDMatch = false;
                                if (lineNoQuot == SelectedSteamID)
                                {
                                    // Most recent ID matches! Start ignoring lines until "}" found.
                                    userIDMatch = true;
                                    continue; // Skip line output
                                }
                            }
                        }
                        else // Currently going through a user
                        {
                            if (lineNoQuot.Contains("}"))
                                completedRemove = true; // Found the end of the user to remove
                            continue; // Skip line output
                        }
                    }
                    newfLoginUsersLines.Add(curline);
                    info = new UTF8Encoding(true).GetBytes(outline + "\n");
                    fs.Write(info, 0, info.Length);
                }
                fLoginUsersLines = newfLoginUsersLines;
            }

            // Remove from list in memory
            MainViewmodel.SteamUsers.Remove(MainViewmodel.SelectedSteamUser);
            listAccounts.Items.Refresh();
        }
        private void AccountItem_Forget(object sender, RoutedEventArgs e)
        {
            DeleteSelected();
        }

        private void listAccounts_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (listAccounts.SelectedIndex != -1 && e.Key == Key.Delete)
                DeleteSelected();
        }
        private void AccountItem_CopySteamID(object sender, RoutedEventArgs e)
        {
            System.Windows.Clipboard.SetText(MainViewmodel.SelectedSteamUser.SteamID);
        }
        private void AccountItem_CopyUsername(object sender, RoutedEventArgs e)
        {
            System.Windows.Clipboard.SetText(MainViewmodel.SelectedSteamUser.AccName);
        }
        private void AccountItem_CopyFriendName(object sender, RoutedEventArgs e)
        {
            System.Windows.Clipboard.SetText(MainViewmodel.SelectedSteamUser.Name);
        }
        private void LoginButtonAnimation(Color colFrom, Color colTo, int len)
        {
            ColorAnimation animation = new ColorAnimation
            {
                From = colFrom, To = colTo, Duration = new Duration(TimeSpan.FromMilliseconds(len))
            };

            btnLogin.Background = new SolidColorBrush(Colors.Orange);
            btnLogin.Background.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        private void btnLogin_MouseEnter(object sender, MouseEventArgs e)
        {
            btnLogin.Background = new SolidColorBrush(MainViewmodel.SelectedSteamUser != null ? Colors.Green : DefaultGray);
        }

        private void btnLogin_MouseLeave(object sender, MouseEventArgs e)
        {
            btnLogin.Background = new SolidColorBrush(MainViewmodel.SelectedSteamUser != null ? DarkGreen : DefaultGray);
        }
        private void btnExit(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnMinimize(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void dragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!Double.IsNaN(this.Height)) // Verifies that the program has started properly. Can be any property to do with the window. Just using Width.
                saveSettings();
        }

        public void ResetSettings()
        {
            persistentSettings = new UserSettings();
            updateFromSettings();
            setAndCheckSteamFolder(false);
            listAccounts.Items.Refresh();

        }
        public void PickSteamFolder()
        {
            bool validSteamFound = (File.Exists(persistentSettings.SteamEXE()));
            string OldLocation = persistentSettings.SteamFolder;

            validSteamFound = setAndCheckSteamFolder(true);
            if (!validSteamFound)
            {
                persistentSettings.SteamFolder = OldLocation;
                MainViewmodel.InputFolderDialogResponse = OldLocation;
                MessageBox.Show($"{Strings.ErrSteamLocation} {OldLocation}");
            }
        }
        public void ResetImages()
        {
            File.Create("DeleteImagesOnStart");
            MessageBox.Show(Strings.InfoReopenImageDl);
            this.Close();
        }
        public bool VACCheckRunning = false;
        public void CheckVac()
        {
            if (!VACCheckRunning)
            {
                VACCheckRunning = true;
                lblStatus.Content = Strings.StatusCheckingVac;

                foreach (Steamuser su in MainViewmodel.SteamUsers)
                {
                    su.vacStatus = Brushes.Transparent;
                }
                listAccounts.Items.Refresh();


                Thread t = new Thread(new ParameterizedThreadStart(checkVacForeach));
                t.Start(MainViewmodel.SteamUsers);
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Environment.Exit(1);
        }

        private void btnShowInfo_Click(object sender, RoutedEventArgs e)
        {
            InfoWindow infoWindow = new InfoWindow();
            infoWindow.DataContext = MainViewmodel;
            infoWindow.Owner = this;
            infoWindow.ShowDialog();
        }

        void checkVacForeach(object oin)
        {
            ObservableCollection<Steamuser> SteamUsers = (ObservableCollection<Steamuser>)oin;
            int currentCount = 0;
            string totalCount = SteamUsers.Count().ToString();

            foreach (Steamuser su in SteamUsers)
            {
                currentCount++;
                this.Dispatcher.Invoke(() =>
                {
                    lblStatus.Content = $"{Strings.StatusCheckingVacActive} {currentCount.ToString()}/{totalCount}";
                });
                //su.vacStatus = GetVacStatus(su.SteamID).Result ? _vacRedBrush : Brushes.Transparent; 
                bool VacOrLimited = false;
                XmlDocument profileXML = new XmlDocument();
                profileXML.Load($"https://steamcommunity.com/profiles/{su.SteamID}?xml=1");
                try
                {
                    bool isVAC = profileXML.DocumentElement != null && profileXML.DocumentElement.SelectNodes("/profile/vacBanned")?[0].InnerText == "1";
                    bool isLimited = profileXML.DocumentElement != null && profileXML.DocumentElement.SelectNodes("/profile/isLimitedAccount")?[0].InnerText == "1";
                    VacOrLimited = isVAC || isLimited;
                }
                catch (NullReferenceException) // User has not set up their account
                {
                    VacOrLimited = false;
                }
                this.Dispatcher.Invoke(() =>
                {
                    su.vacStatus = VacOrLimited ? _vacRedBrush : Brushes.Transparent;
                    UpdateListFromAsyncVacCheck(su);
                });
            }

            this.Dispatcher.Invoke(() =>
            {
                lblStatus.Content = Strings.StatusReady;
                saveVacInformation();
                VACCheckRunning = false;
            });
        }
        void UpdateListFromAsyncVacCheck(Steamuser UpdatedUser)
        {
            foreach (Steamuser su in MainViewmodel.SteamUsers)
            {
                if (su.SteamID == UpdatedUser.SteamID)
                {
                    su.vacStatus = UpdatedUser.vacStatus;
                }
            }
            listAccounts.Items.Refresh();
        }
        void saveVacInformation()
        {
            if (!Double.IsNaN(this.Height))
            {
                // Verifies that the program has started properly. Can be any property to do with the window. Just using Width.

                Dictionary<string, bool> VacInformation = new Dictionary<string, bool> { };
                foreach (Steamuser su in MainViewmodel.SteamUsers)
                {
                    VacInformation.Add(su.SteamID, su.vacStatus == _vacRedBrush ? true : false); // If red >> Vac or Limited
                }

                JsonSerializer serializer = new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore };

                using (StreamWriter sw = new StreamWriter(@"SteamVACCache.json"))
                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    serializer.Serialize(writer, VacInformation);
                }
            }
        }
        void loadVacInformation()
        {
            JsonSerializer serializer = new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore };
            using (StreamReader sr = new StreamReader(@"SteamVACCache.json"))
            {
                Dictionary<string, bool> VacInformation = JsonConvert.DeserializeObject<Dictionary<string, bool>>(sr.ReadToEnd());
                foreach (Steamuser su in MainViewmodel.SteamUsers)
                {
                    if (VacInformation.ContainsKey(su.SteamID))
                        su.vacStatus = VacInformation[su.SteamID] ? _vacRedBrush : Brushes.Transparent;
                    else
                        su.vacStatus = Brushes.Transparent;
                }
            }
        }
        private void btnNewLogin_Click(object sender, RoutedEventArgs e)
        {
            // Kill Steam
            closeSteam();
            // Set all accounts to 'not used last' status
            UpdateLoginusers(true);
            // Start Steam
            if (persistentSettings.StartAsAdmin)
                Process.Start(persistentSettings.SteamEXE());
            else
                Process.Start(new ProcessStartInfo("explorer.exe", persistentSettings.SteamEXE()));
            lblStatus.Content = Strings.StatusStartedSteam;
        }
        private void chkShowSettings_Click(object sender, RoutedEventArgs e)
        {
            Settings settingsDialog = new Settings();
            settingsDialog.ShareMainWindow(this);
            settingsDialog.DataContext = MainViewmodel;
            settingsDialog.Owner = this;
            settingsDialog.ShowDialog();
        }
        public void toggleVACStatus(bool VACEnabled)
        {
            if (!VACEnabled)
            {
                foreach (Steamuser su in MainViewmodel.SteamUsers)
                {
                    su.vacStatus = Brushes.Transparent;
                }
            }
            else if (File.Exists("SteamVACCache.json"))
            {
                loadVacInformation();
            }
            listAccounts.Items.Refresh();
            persistentSettings.ShowVACStatus = VACEnabled;
            MainViewmodel.ShowVACStatus = VACEnabled;
        }
        private void resourceClean(bool update)
        {
            new DirectoryInfo("Resources").Delete(true);
            string[] delFileNames;
            if (update)
                delFileNames = new string[] { "x64.zip", "x32.zip", "upd.7z", "UpdateInformation.txt" };
            else
                delFileNames = new string[] { "x64.zip", "x32.zip", "upd.7z" };

            foreach (string f in delFileNames)
            {
                if (File.Exists(f))
                    File.Delete(f);
            }

            if (update)
            {
                bool deleted = false;
                while (!deleted)
                {
                    try
                    {
                        File.Delete("TcNo-Acc-Switcher-Updater.exe");
                        deleted = true;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(500);
                    }
                }
            }
        }
        private void singleFileUpdateClean()
        {
            string[] delFileNames = new string[] { "TcNo Account Switcher.deps.json", "TcNo Account Switcher.dll", "TcNo Account Switcher.runtimeconfig.json", "TcNo-Acc-Switcher-Updater.dll", "TcNo-Acc-Switcher-Updater.runtimeconfig.json" };
            try
            {
                foreach (string f in delFileNames)
                {
                    if (File.Exists(f))
                        File.Delete(f);
                }
            }
            catch (Exception) { }
        }

        private void CheckShortcuts()
        {
            MainViewmodel.DesktopShortcut = shortcutExist(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            MainViewmodel.StartWithWindows = CheckStartWithWindows();
            MainViewmodel.StartMenuIcon = shortcutExist(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), @"TcNo Account Switcher\"));
        }
        private bool CheckStartWithWindows()
        {
            using (TaskService ts = new TaskService())
            {
                TaskCollection tasks = ts.RootFolder.Tasks;
                return tasks.Exists("TcNo Account Switcher - Tray start with logon");
            }
        }
        public void DesktopShortcut(bool bEnabled)
        {
            MainViewmodel.DesktopShortcut = bEnabled;
            string desktop_path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (bEnabled)
                createShortcut(desktop_path);
            else
                deleteShortcut(desktop_path, "TcNo Account Switcher.lnk", false);
        }
        public void StartWithWindows(bool bEnabled)
        {
            MainViewmodel.StartWithWindows = bEnabled;

            extractTrayExe();

            if (bEnabled)
            {
                if (!CheckStartWithWindows())
                {
                    TaskService ts = new TaskService();
                    TaskDefinition td = ts.NewTask();
                    td.Principal.RunLevel = TaskRunLevel.Highest;
                    td.Triggers.AddNew(TaskTriggerType.Logon);
                    string program_path = Path.GetFullPath("TcNo Account Switcher Tray.exe");
                    td.Actions.Add(new ExecAction(program_path, null));
                    ts.RootFolder.RegisterTaskDefinition("TcNo Account Switcher - Tray start with logon", td);
                    MessageBox.Show(Strings.InfoTrayWindowsStart);
                }
            }
            else
            {
                TaskService ts = new TaskService();
                ts.RootFolder.DeleteTask("TcNo Account Switcher - Tray start with logon");
                MessageBox.Show(Strings.InfoTrayWindowsStartOff);
            }


            //string startup_path = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            //if (bEnabled)
            //    createShortcut(startup_path);
            //else
            //    deleteShortcut(startup_path, "TcNo Account Switcher.lnk", false);
        }
        public void StartMenuShortcut(bool bEnabled)
        {
            MainViewmodel.StartMenuIcon = bEnabled;
            string programs_path = Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                   shortcutFolder = Path.Combine(programs_path, @"TcNo Account Switcher\");
            if (bEnabled)
            {
                createShortcut(shortcutFolder);
                createTrayShortcut(shortcutFolder);
            }
            else
            {
                deleteShortcut(shortcutFolder, "TcNo Account Switcher.lnk", false);
                deleteShortcut(shortcutFolder, "TcNo Account Switcher - System tray.lnk", true);
            }
        }
        private bool shortcutExist(string location)
        {
            string settingsShortcut = Path.Combine(location, "TcNo Account Switcher.lnk");
            return File.Exists(settingsShortcut);
        }
        private void createShortcut(string location)
        {
            if (!Directory.Exists(location))
            {
                Directory.CreateDirectory(location);
            }
            // Has to be done in such a strange way because .NET Core points it to the .DLL inside of %temp% instead of the actual .exe...
            string selfexe = Path.Combine(Directory.GetCurrentDirectory(), System.AppDomain.CurrentDomain.FriendlyName), // Changes .dll to .exe. .NET Core returns the .dll instead of the .exe required for the shortcut.
                   selflocation = Directory.GetCurrentDirectory(),
                   iconDirectory = Path.Combine(selflocation, "icon.ico"),
                   settingsLink = Path.Combine(location, "TcNo Account Switcher.lnk"),
                   description = "TcNo Account Switcher";

            writeShortcut(location, selfexe, selflocation, iconDirectory, description, settingsLink);
        }
        private void extractTrayExe()
        {
            // Extract tray .exe from compressed .7z resource.
            int curTrayVersion = 0;
            if (File.Exists("trayversion"))
                curTrayVersion = int.Parse(File.ReadAllText("trayversion"));
            if (trayversion > curTrayVersion || !File.Exists("TcNo Account Switcher Tray.exe")) // Update
            {
                extract7zip();
                string zPath = Path.Combine("Resources", "7za.exe"),
                        trayzip = "tray.7z",
                        ePath = Directory.GetCurrentDirectory();
#if X64
                File.WriteAllBytes(trayzip, Properties.Resources.tray64);
#else
                File.WriteAllBytes(trayzip, Properties.Resources.tray32);
#endif

                ProcessStartInfo pro = new ProcessStartInfo();
                pro.WindowStyle = ProcessWindowStyle.Hidden;
                pro.FileName = zPath;
                pro.Arguments = string.Format("x \"{0}\" -y -o\"{1}\"", trayzip, ePath);
                pro.UseShellExecute = false;
                pro.RedirectStandardOutput = true;
                pro.CreateNoWindow = true;
                Process x = Process.Start(pro);
                x.WaitForExit();

                File.WriteAllText("trayversion", trayversion.ToString());
                File.Delete(trayzip);
                resourceClean(false);
            }
        }
        private void createTrayShortcut(string location)
        {
            extractTrayExe();

            string selfexe = Path.Combine(Directory.GetCurrentDirectory(), "TcNo Account Switcher Tray.exe"), // Changes .dll to .exe. .NET Core returns the .dll instead of the .exe required for the shortcut.
                selflocation = Directory.GetCurrentDirectory(),
                iconDirectory = Path.Combine(selflocation, "icon.ico"),
                settingsLink = Path.Combine(location, "TcNo Account Switcher - System tray.lnk"),
                description = "TcNo Account Switcher - System tray";

            writeShortcut(location, selfexe, selflocation, iconDirectory, description, settingsLink);
        }
        private void writeShortcut(string location, string exe, string selflocation, string iconDirectory, string description, string settingsLink)
        {
            if (!File.Exists(settingsLink))
            {
                if (File.Exists("CreateShortcut.vbs"))
                    File.Delete("CreateShortcut.vbs");

                using (FileStream fs = new FileStream(iconDirectory, FileMode.Create))
                    Properties.Resources.icon.Save(fs);


                string[] Lines = {"set WshShell = WScript.CreateObject(\"WScript.Shell\")",
                       "set oShellLink = WshShell.CreateShortcut(\"" + settingsLink  + "\")",
                       "oShellLink.TargetPath = \"" + exe + "\"",
                       "oShellLink.WindowStyle = 1",
                       "oShellLink.IconLocation = \"" + iconDirectory + "\"",
                       "oShellLink.Description = \"" + description + "\"",
                       "oShellLink.WorkingDirectory = \"" + selflocation + "\"",
                       "oShellLink.Save()"
            };
                File.WriteAllLines("CreateShortcut.vbs", Lines);


                string result_string = "";
                Process vbsProcess = new Process();

                vbsProcess.StartInfo.FileName = "cscript";
                vbsProcess.StartInfo.Arguments = "//nologo \"" + Path.Combine(selflocation, "CreateShortcut.vbs") + "\"";
                vbsProcess.StartInfo.UseShellExecute = false;
                vbsProcess.StartInfo.RedirectStandardOutput = true;
                vbsProcess.StartInfo.CreateNoWindow = true;

                vbsProcess.Start();
                result_string = vbsProcess.StandardOutput.ReadToEnd();
                vbsProcess.Close();

                result_string = result_string.Replace("\r\n", "");
                File.Delete("CreateShortcut.vbs");
                MessageBox.Show("Shortcut created!\n\nLocation: " + settingsLink);
            }
        }
        private void deleteShortcut(string location, string name, bool delFolder)
        {
            string settingsLink = Path.Combine(location, name);
            if (File.Exists(settingsLink))
                File.Delete(settingsLink);
            if (delFolder)
            {
                if (Directory.GetFiles(location).Length == 0)
                    Directory.Delete(location);
                else
                    MessageBox.Show($"{Strings.ErrDeleteFolderNonempty} {location}");
            }
            MessageBox.Show(Strings.InfoShortcutDeleted.Replace("{}", name));
        }


        //void DownloadImages(object oin)
        //{
        //    List<Steamuser> ImagesToDownload = (List<Steamuser>)oin;
        //private static async Task<bool> GetVacStatus(string steamID)
        //{
        //    XmlDocument profileXML = new XmlDocument();
        //    profileXML.Load($"https://steamcommunity.com/profiles/{steamID}?xml=1");
        //    try
        //    {
        //        bool isVAC = profileXML.DocumentElement.SelectNodes("/profile/vacBanned")[0].InnerText == "1" ? true : false;
        //        bool isLimited = profileXML.DocumentElement.SelectNodes("/profile/isLimitedAccount")[0].InnerText == "1" ? true : false;
        //        return isVAC || isLimited;
        //    }
        //    catch (NullReferenceException) // User has not set up their account
        //    {
        //        return false;
        //    }
        //}
    }
}
