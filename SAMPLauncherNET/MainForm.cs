﻿using MaterialSkin;
using MaterialSkin.Controls;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinFormsTranslator;

/// <summary>
/// SA:MP launcher .NET namespace
/// </summary>
namespace SAMPLauncherNET
{
    /// <summary>
    /// Main form class
    /// </summary>
    public partial class MainForm : MaterialForm
    {

        /// <summary>
        /// Load servers
        /// </summary>
        private readonly List<KeyValuePair<Server, int>> loadServers = new List<KeyValuePair<Server, int>>();

        /// <summary>
        /// Loaded servers
        /// </summary>
        private readonly List<KeyValuePair<Server, int>> loadedServers = new List<KeyValuePair<Server, int>>();

        /// <summary>
        /// Registered servers
        /// </summary>
        private readonly Dictionary<string, Server> registeredServers = new Dictionary<string, Server>();

        /// <summary>
        /// Servers thread
        /// </summary>
        private Thread serversThread;

        /// <summary>
        /// Row thread
        /// </summary>
        private Thread rowThread;

        /// <summary>
        /// Row thread success
        /// </summary>
        private bool rowThreadSuccess;

        /// <summary>
        /// APIs
        /// </summary>
        private List<ServerListConnector> apis = new List<ServerListConnector>();

        /// <summary>
        /// Configuration
        /// </summary>
        private readonly SAMPConfig config = SAMP.SAMPConfig;

        /// <summary>
        /// Query first server
        /// </summary>
        private bool queryFirstServer = true;

        /// <summary>
        /// Selected API index
        /// </summary>
        private int selectedAPIIndex = -1;

        /// <summary>
        /// Loaded gallery
        /// </summary>
        private readonly Dictionary<string, Image> loadedGallery = new Dictionary<string, Image>();

        /// <summary>
        /// Gallery thread
        /// </summary>
        private Thread galleryThread;

        /// <summary>
        /// Last gallery image index
        /// </summary>
        private uint lastGalleryImageIndex;

        /// <summary>
        /// Selected browser
        /// </summary>
        public Server SelectedServer
        {
            get
            {
                Server ret = null;
                foreach (DataGridViewRow dgvr in serversGridView.SelectedRows)
                {
                    if (dgvr.Cells[dgvr.Cells.Count - 1].Value != null)
                    {
                        string ipp = dgvr.Cells[dgvr.Cells.Count - 1].Value.ToString();
                        if (registeredServers.ContainsKey(ipp))
                        {
                            ret = registeredServers[ipp];
                            break;
                        }
                    }
                }
                return ret;
            }
        }

        /// <summary>
        /// Is in favourites list
        /// </summary>
        private bool IsInFavouriteList
        {
            get
            {
                bool ret = false;
                if ((selectedAPIIndex >= 0) && (selectedAPIIndex < apis.Count))
                {
                    ServerListConnector slc = apis[selectedAPIIndex];
                    ret = ((slc.ServerListType == EServerListType.Favourites) || (slc.ServerListType == EServerListType.LegacyFavourites));
                }
                return ret;
            }
        }

        /// <summary>
        /// Favourite lists
        /// </summary>
        public IEnumerable<IndexedServerListConnector> FavouriteLists
        {
            get
            {
                List<IndexedServerListConnector> ret = new List<IndexedServerListConnector>();
                for (int i = 0; i < apis.Count; i++)
                {
                    ServerListConnector slc = apis[i];
                    if ((slc.ServerListType == EServerListType.Favourites) || (slc.ServerListType == EServerListType.LegacyFavourites))
                    {
                        ret.Add(new IndexedServerListConnector(slc, i));
                    }
                }
                return ret;
            }
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            Size sz = revertConfigButton.Size;
            Translator.LoadTranslation(this);
            revertConfigButton.Location = new Point(revertConfigButton.Location.X + (sz.Width - revertConfigButton.Size.Width), revertConfigButton.Location.Y);
            Translator.EnumerableToComboBox(languagesComboBox, Translator.TranslatorInterface.Languages);
            serversGridView.Columns[1].HeaderText = Translator.GetTranslation("PING");
            serversGridView.Columns[2].HeaderText = Translator.GetTranslation("HOSTNAME");
            serversGridView.Columns[3].HeaderText = Translator.GetTranslation("PLAYERS");
            serversGridView.Columns[4].HeaderText = Translator.GetTranslation("MODE");
            serversGridView.Columns[5].HeaderText = Translator.GetTranslation("LANGUAGE");
            serversGridView.Columns[6].HeaderText = Translator.GetTranslation("IP_AND_PORT");
            playersGridView.Columns[0].HeaderText = Translator.GetTranslation("PLAYER");
            playersGridView.Columns[1].HeaderText = Translator.GetTranslation("SCORE");
            rulesGridView.Columns[0].HeaderText = Translator.GetTranslation("RULE");
            rulesGridView.Columns[1].HeaderText = Translator.GetTranslation("VALUE");
            apiGridView.Columns[1].HeaderText = Translator.GetTranslation("NAME");
            apiGridView.Columns[2].HeaderText = Translator.GetTranslation("TYPE");
            apiGridView.Columns[3].HeaderText = Translator.GetTranslation("ENDPOINT");

            int i = 0;
            foreach (Language language in Translator.TranslatorInterface.Languages)
            {
                if (language.Culture == (string)(Properties.Settings.Default["Language"]))
                {
                    languagesComboBox.SelectedIndex = i;
                    break;
                }
                ++i;
            }
            assemblyVersionLabel.Text += ": " + Assembly.GetExecutingAssembly().GetName().Version;
            fileVersionLabel.Text += ": " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
            productVersionLabel.Text += ": " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;

            MaterialSkinManager material_skin_manager = MaterialSkinManager.Instance;
            material_skin_manager.AddFormToManage(this);
            material_skin_manager.Theme = MaterialSkinManager.Themes.DARK;
            material_skin_manager.ColorScheme = new ColorScheme(Primary.Blue700, Primary.Blue800, Primary.Blue500, Accent.LightBlue200, TextShade.WHITE);

            galleryFileSystemWatcher.Path = SAMP.GalleryPath;
            textFileSystemWatcher.Path = SAMP.ConfigPath;

            lastChatlogTextBox.Text = SAMP.Chatlog;
            savedPositionsTextBox.Text = SAMP.SavedPositions;

            ReloadVersions();
            ReloadSAMPConfig();
            ReloadAPIs();
            ReloadLauncherConfig();
            ReloadDeveloperToolsConfig();
            serversThread = new Thread(() =>
            {
                while (true)
                {
                    List<KeyValuePair<Server, int>> rlist = new List<KeyValuePair<Server, int>>();
                    lock (loadServers)
                    {
                        foreach (KeyValuePair<Server, int> kv in loadServers)
                        {
                            if ((kv.Key is FavouriteServer) || (kv.Key is BackendRESTfulServer))
                            {
                                rlist.Add(kv);
                            }
                            else
                            {
                                if (kv.Key.IsDataFetched(ERequestType.Ping) || kv.Key.IsDataFetched(ERequestType.Information))
                                {
                                    rlist.Add(kv);
                                }
                                else
                                {
                                    kv.Key.SendQueryWhenExpired(ERequestType.Ping, 5000U);
                                    kv.Key.SendQueryWhenExpired(ERequestType.Information, 5000U);
                                }
                            }
                        }
                        foreach (KeyValuePair<Server, int> kv in rlist)
                        {
                            loadServers.Remove(kv);
                        }
                    }
                    lock (loadedServers)
                    {
                        loadedServers.AddRange(rlist);
                    }
                    rlist.Clear();
                }
            });
            serversThread.Start();
        }

        /// <summary>
        /// Clear APIs
        /// </summary>
        private void ClearAPIs()
        {
            selectAPIComboBox.Items.Clear();
            selectedAPIIndex = -1;
            lock (apis)
            {
                apis.Clear();
            }
            apiDataTable.Clear();
            lock (loadServers)
            {
                loadServers.Clear();
            }
            lock (loadedServers)
            {
                loadedServers.Clear();
            }
            lock (registeredServers)
            {
                registeredServers.Clear();
            }
            serversDataTable.Clear();
        }

        /// <summary>
        /// Reload APIs
        /// </summary>
        private void ReloadAPIs()
        {
            ClearAPIs();
            lock (apis)
            {
                apis = SAMP.APIIO;
                lock (apiDataTable)
                {
                    for (int i = 0; i < apis.Count; i++)
                    {
                        DataRow dr = apiDataTable.NewRow();
                        ServerListConnector slc = apis[i];
                        string trimmed_name = slc.Name.Trim();
                        if (trimmed_name.Length > 4)
                        {
                            if (trimmed_name.StartsWith("{$") && trimmed_name.EndsWith("$}"))
                            {
                                slc.TranslatableText = Translator.GetTranslation(trimmed_name.Substring(2, trimmed_name.Length - 4));
                            }
                        }
                        dr.ItemArray = new object[] { i, slc.Name, slc.ServerListType.ToString(), slc.Endpoint };
                        apiDataTable.Rows.Add(dr);
                    }
                }
                selectAPIComboBox.Items.AddRange(apis.ToArray());
                if (apis.Count > 0)
                {
                    selectAPIComboBox.SelectedIndex = 0;
                }
            }
        }

        /// <summary>
        /// Select API
        /// </summary>
        private void SelectAPI()
        {
            int index = selectAPIComboBox.SelectedIndex;
            if ((index >= 0) && (index < apis.Count))
            {
                lock (loadServers)
                {
                    queryFirstServer = true;
                    ServerListConnector slc = apis[index];
                    if (slc.NotLoaded)
                    {
                        slc.NotLoaded = false;
                        Dictionary<string, Server> l = slc.ServerListIO;
                        foreach (Server server in l.Values)
                        {
                            loadServers.Add(new KeyValuePair<Server, int>(server, index));
                        }
                    }
                }
                UpdateServerListFilter();
                UpdateServerCount();
                EnterRow();
            }
        }

        /// <summary>
        /// Update server count
        /// </summary>
        private void UpdateServerCount()
        {
            uint c = 0U;
            int index = selectedAPIIndex;
            if ((index >= 0) && (index < apis.Count))
            {
                c = apis[index].ServerCount;
            }
            serverCountLabel.Text = Translator.GetTranslation("SERVERS") + ": " + c;
        }

        /// <summary>
        /// Enter row
        /// </summary>
        /// <returns>Success</returns>
        private bool EnterRow()
        {
            bool ret = false;
            if (rowThread != null)
            {
                rowThread.Abort();
                rowThread = null;
            }
            rowThreadSuccess = false;
            Server server = SelectedServer;
            if (server != null)
            {
                if (server.IsDataFetched(ERequestType.Ping) && server.IsDataFetched(ERequestType.Information) && server.IsDataFetched(ERequestType.Rules) && server.IsDataFetched(ERequestType.Clients))
                {
                    ReloadSelectedServerRow();
                }
                rowThread = new Thread(() => RequestServerInfo(server));
                rowThread.Start();
                ret = true;
            }
            return ret;
        }

        /// <summary>
        /// Reload selected server row
        /// </summary>
        private void ReloadSelectedServerRow()
        {
            foreach (DataGridViewRow dgvr in serversGridView.SelectedRows)
            {
                if (dgvr.Cells[dgvr.Cells.Count - 1].Value != null)
                {
                    string ipp = dgvr.Cells[dgvr.Cells.Count - 1].Value.ToString();
                    if (registeredServers.ContainsKey(ipp))
                    {
                        Server server = registeredServers[ipp];
                        DateTime timestamp = DateTime.Now;
                        while ((!server.IsDataFetched(ERequestType.Ping)) && (!server.IsDataFetched(ERequestType.Information)))
                        {
                            if (DateTime.Now.Subtract(timestamp).TotalMilliseconds >= 1000)
                            {
                                break;
                            }
                        }
                        if (server.IsDataFetched(ERequestType.Ping))
                        {
                            dgvr.Cells[1].Value = server.Ping;
                        }
                        if (server.IsDataFetched(ERequestType.Information))
                        {
                            dgvr.Cells[2].Value = server.Hostname;
                            dgvr.Cells[3].Value = new PlayerCountString(server.PlayerCount, server.MaxPlayers);
                            dgvr.Cells[4].Value = server.Gamemode;
                            dgvr.Cells[5].Value = server.Language;
                        }
                        ReloadServerInfo();
                    }
                }
                break;
            }
        }

        /// <summary>
        /// Reload server information
        /// </summary>
        private void ReloadServerInfo()
        {
            int si = 0;
            bool cs = false;
            Server server = SelectedServer;
            if (server != null)
            {
                foreach (DataGridViewRow dgvr in playersGridView.SelectedRows)
                {
                    si = dgvr.Index;
                    break;
                }
                playersDataTable.Clear();
                try
                {
                    int i = 0;
                    foreach (string client in server.ClientKeys)
                    {
                        DataRow dr = playersDataTable.NewRow();
                        object[] row = new object[2];
                        row[0] = client;
                        row[1] = server.GetScoreFromClient(client);
                        dr.ItemArray = row;
                        playersDataTable.Rows.Add(dr);
                        if (i == si)
                        {
                            cs = true;
                        }
                        ++i;
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message);
                }
                if (cs)
                {
                    playersGridView.Rows[si].Selected = true;
                }
                si = 0;
                cs = false;
                foreach (DataGridViewRow dgvr in rulesGridView.SelectedRows)
                {
                    si = dgvr.Index;
                    break;
                }
                rulesDataTable.Clear();
                try
                {
                    int i = 0;
                    foreach (string rule in server.RuleKeys)
                    {
                        DataRow dr = rulesDataTable.NewRow();
                        object[] row = new object[2];
                        row[0] = rule;
                        row[1] = server.GetRuleValue(rule);
                        dr.ItemArray = row;
                        rulesDataTable.Rows.Add(dr);
                        if (i == si)
                        {
                            cs = true;
                        }
                        ++i;
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message);
                }
                if (cs)
                {
                    rulesGridView.Rows[si].Selected = true;
                }
            }
        }

        /// <summary>
        /// Request server information
        /// </summary>
        /// <param name="server">Server</param>
        private void RequestServerInfo(Server server)
        {
            while (true)
            {
                server.FetchMultiData(new ERequestType[] { ERequestType.Ping, ERequestType.Information, ERequestType.Clients, ERequestType.Rules });
                rowThreadSuccess = (server.IsDataFetched(ERequestType.Ping) && server.IsDataFetched(ERequestType.Information) && server.IsDataFetched(ERequestType.Clients) && server.IsDataFetched(ERequestType.Rules));
                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// Escape filter string
        /// </summary>
        /// <param name="str">String</param>
        /// <returns>Escaped filer string</returns>
        private static string EscapeFilterString(string str)
        {
            StringBuilder ret = new StringBuilder();
            foreach (char c in str)
            {
                switch (c)
                {
                    case '\'':
                        break;
                    case '\\':
                        ret.Append("\\\\");
                        break;
                    case '%':
                        ret.Append("\\%");
                        break;
                    default:
                        ret.Append(c, 1);
                        break;
                }
            }
            return ret.ToString();
        }

        /// <summary>
        /// Update server list filter
        /// </summary>
        private void UpdateServerListFilter()
        {
            string ft = EscapeFilterString(filterSingleLineTextField.Text.Trim());
            StringBuilder filter = new StringBuilder("GroupID=");
            filter.Append(selectedAPIIndex.ToString());
            if (ft.Length > 0)
            {
                if (filterHostnameRadioButton.Checked)
                {
                    filter.Append(" AND `Hostname` LIKE '%");
                }
                else if (filterModeRadioButton.Checked)
                {
                    filter.Append(" AND `Mode` LIKE '%");
                }
                else if (filterLanguageRadioButton.Checked)
                {
                    filter.Append(" AND `Language` LIKE '%");
                }
                else
                {
                    filter.Append(" AND `IP and port` LIKE '%");
                }
                filter.Append(ft);
                filter.Append("%'");
            }
            serversBindingSource.Filter = filter.ToString();
            EnterRow();
        }

        /// <summary>
        /// Reload favourites
        /// </summary>
        /// <param name="servers">Servers</param>
        /// <param name="index">Index</param>
        private void ReloadFavourites(Dictionary<string, Server> servers, int index)
        {
            DataRow[] rows = serversDataTable.Select("GroupID=" + index);
            foreach (DataRow row in rows)
            {
                row.Delete();
            }
            lock (loadServers)
            {
                foreach (Server server in servers.Values)
                {
                    loadServers.Add(new KeyValuePair<Server, int>(server, index));
                }
            }
        }

        /// <summary>
        /// Connect
        /// </summary>
        /// <param name="rconPassword">RCON password</param>
        private void Connect(Server server = null, string rconPassword = null, bool quitWhenDone = false)
        {
            Server s = server;
            if (s == null)
            {
                s = SelectedServer;
            }
            if (s != null)
            {
                ConnectForm cf = new ConnectForm(!(s.HasPassword));
                DialogResult result = cf.ShowDialog();
                DialogResult = DialogResult.None;
                if (result == DialogResult.OK)
                {
                    SAMP.LaunchSAMP(s, cf.Username, s.HasPassword ? cf.ServerPassword : null, rconPassword, false, quitWhenDone, useDiscordRichPresenceCheckBox.Checked, this);
                }
            }
        }

        /// <summary>
        /// Visit website
        /// </summary>
        /// <param name="url">URL</param>
        private static void VisitWebsite(string url)
        {
            if (!(url.Contains("://")))
            {
                url = "http://" + url;
            }
            if (MessageBox.Show(url + "\r\n\r\n" + Translator.GetTranslation("VISIT_WEBSITE_MESSAGE"), Translator.GetTranslation("VISIT_WEBSITE_TITLE") + " " + url, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Process.Start(url);
            }
        }

        /// <summary>
        /// Reload launcher configuration
        /// </summary>
        private void ReloadLauncherConfig()
        {
            LauncherConfigDataContract lcdc = SAMP.LauncherConfigIO;
            developmentDirectorySingleLineTextField.Text = lcdc.DevelopmentDirectory;
            selectedAPIIndex = lcdc.LastSelectedServerListAPI;
            selectAPIComboBox.SelectedIndex = selectedAPIIndex;
        }

        /// <summary>
        /// Reload gallery
        /// </summary>
        private void ReloadGallery()
        {
            if (galleryThread != null)
            {
                galleryThread.Abort();
            }
            galleryListView.Clear();
            galleryImageList.Images.Clear();
            loadedGallery.Clear();
            lastGalleryImageIndex = 0U;
            galleryThread = new Thread(() =>
            {
                foreach (string path in SAMP.GalleryImagePaths)
                {
                    Image image = ThumbnailsCache.GetThumbnail(path);
                    if (image != null)
                    {
                        lock (loadedGallery)
                        {
                            loadedGallery.Add(path, image);
                        }
                    }
                }
            });
            galleryThread.Start();
        }

        /// <summary>
        /// View selected image
        /// </summary>
        private void ViewSelectedImage()
        {
            foreach (ListViewItem item in galleryListView.SelectedItems)
            {
                string file_name = (string)(item.Tag);
                if (File.Exists(file_name))
                {
                    Process.Start(file_name);
                }
            }
        }

        /// <summary>
        /// Delete selected image
        /// </summary>
        private void DeleteSelectedImage()
        {
            List<string> files = new List<string>();
            foreach (ListViewItem item in galleryListView.SelectedItems)
            {
                string file_name = (string)(item.Tag);
                if (File.Exists(file_name))
                {
                    files.Add(file_name);
                }
            }
            if (files.Count > 0)
            {
                if (MessageBox.Show(Translator.GetTranslation("DELETE_SELECTED_IMAGES"), Translator.GetTranslation("DELETE_SELECTED_IMAGES_TITLE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    galleryFileSystemWatcher.EnableRaisingEvents = false;
                    foreach (string file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine(e.Message);
                        }
                    }
                    galleryFileSystemWatcher.EnableRaisingEvents = true;
                    ReloadGallery();
                }
            }
        }

        /// <summary>
        /// Save configuration
        /// </summary>
        public void SaveConfig()
        {
            config.PageSize = int.Parse(pageSizeSingleLineTextField.Text);
            config.FPSLimit = fpsLimitTrackBar.Value;
            config.DisableHeadMovement = disableHeadMoveCheckBox.Checked;
            config.Timestamp = timestampCheckBox.Checked;
            config.IME = imeCheckBox.Checked;
            config.MultiCore = multiCoreCheckBox.Checked;
            config.DirectMode = directModeCheckBox.Checked;
            config.AudioMessageOff = audioMessageOffCheckBox.Checked;
            config.AudioProxyOff = audioProxyOffCheckBox.Checked;
            config.NoNametagStatus = noNametagStatusCheckBox.Checked;
            config.FontFace = fontFaceSingleLineTextField.Text;
            config.FontWeight = fontWeightCheckBox.Checked;
            config.Save();
            LauncherConfigDataContract lcdc = SAMP.LauncherConfigIO;
            lcdc.UseDiscordRichPresence = useDiscordRichPresenceCheckBox.Checked;
            SAMP.LauncherConfigIO = lcdc;
        }

        /// <summary>
        /// Reload versions
        /// </summary>
        private void ReloadVersions()
        {
            SAMPVersionDataContract current_version = SAMPProvider.CurrentVersion;
            versionsListView.Clear();
            foreach (SAMPVersionDataContract version in SAMPProvider.VersionsList)
            {
                ListViewItem lvi = versionsListView.Items.Add(version.ToString());
                lvi.Tag = version;
                lvi.ImageIndex = ((current_version == version) ? 0 : 1);
            }
            ReloadAPIs();
        }

        /// <summary>
        /// Reload SA:MP configuration
        /// </summary>
        private void ReloadSAMPConfig()
        {
            pageSizeSingleLineTextField.Text = config.PageSize.ToString();
            fpsLimitTrackBar.Value = config.FPSLimit;
            fpsLimitSingleLineTextField.Text = fpsLimitTrackBar.Value.ToString();
            disableHeadMoveCheckBox.Checked = config.DisableHeadMovement;
            timestampCheckBox.Checked = config.Timestamp;
            imeCheckBox.Checked = config.IME;
            multiCoreCheckBox.Checked = config.MultiCore;
            directModeCheckBox.Checked = config.DirectMode;
            audioMessageOffCheckBox.Checked = config.AudioMessageOff;
            audioProxyOffCheckBox.Checked = config.AudioProxyOff;
            noNametagStatusCheckBox.Checked = config.NoNametagStatus;
            fontFaceSingleLineTextField.Text = config.FontFace;
            fontWeightCheckBox.Checked = config.FontWeight;
            useDiscordRichPresenceCheckBox.Checked = SAMP.LauncherConfigIO.UseDiscordRichPresence;
        }

        /// <summary>
        /// Fill items in checked list box
        /// </summary>
        /// <param name="checkedListBox">Checked list box</param>
        /// <param name="files">Files</param>
        /// <param name="checkedFiles">Checked files</param>
        private static void FillItemsInCheckedListBox(CheckedListBox checkedListBox, FileResource[] files, string[] checkedFiles)
        {
            checkedListBox.Items.AddRange(files);
            for (int i = 0; i < files.Length; i++)
            {
                FileResource file = files[i];
                foreach (string checked_file in checkedFiles)
                {
                    if (file.FileNameWithoutExtension == checked_file)
                    {
                        checkedListBox.SetItemChecked(i, true);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Reload developer tools configuration
        /// </summary>
        private void ReloadDeveloperToolsConfig()
        {
            DeveloperToolsConfigDataContract dtcdc = SAMP.DeveloperToolsConfigIO;
            developerToolsGamemodesCheckedListBox.Items.Clear();
            developerToolsFilterscriptsCheckedListBox.Items.Clear();
            developerToolsPluginsCheckedListBox.Items.Clear();
            string directory = Utils.AppendCharacterToString(developmentDirectorySingleLineTextField.Text, '\\');
            if (Directory.Exists(directory))
            {
                FillItemsInCheckedListBox(developerToolsGamemodesCheckedListBox, Utils.GetFilesFromDirectory(directory + "gamemodes", "*.amx", SearchOption.TopDirectoryOnly), dtcdc.Gamemodes);
                FillItemsInCheckedListBox(developerToolsFilterscriptsCheckedListBox, Utils.GetFilesFromDirectory(directory + "filterscripts", "*.amx", SearchOption.TopDirectoryOnly), dtcdc.Filterscripts);
                FillItemsInCheckedListBox(developerToolsPluginsCheckedListBox, Utils.GetFilesFromDirectory(directory + "plugins", "*.dll", SearchOption.TopDirectoryOnly), dtcdc.Plugins);
            }
        }

        /// <summary>
        /// Save developer tools configuration
        /// </summary>
        private void SaveDeveloperToolsConfig()
        {
            DeveloperToolsConfigDataContract dtcdc = SAMP.DeveloperToolsConfigIO;
            List<string> entries = new List<string>();
            foreach (var item in developerToolsGamemodesCheckedListBox.CheckedItems)
            {
                entries.Add(item.ToString());
            }
            dtcdc.Gamemodes = entries.ToArray();
            entries.Clear();
            foreach (var item in developerToolsFilterscriptsCheckedListBox.CheckedItems)
            {
                entries.Add(item.ToString());
            }
            dtcdc.Filterscripts = entries.ToArray();
            entries.Clear();
            foreach (var item in developerToolsPluginsCheckedListBox.CheckedItems)
            {
                entries.Add(item.ToString());
            }
            dtcdc.Plugins = entries.ToArray();
            entries.Clear();
            dtcdc.Hostname = developerToolsHostnameSingleLineTextField.Text;
            dtcdc.Port = Utils.GetInt(developerToolsPortSingleLineTextField.Text);
            dtcdc.Password = developerToolsServerPasswordSingleLineTextField.Text;
            dtcdc.RCONPassword = developerToolsRCONPasswordSingleLineTextField.Text;
            if (dtcdc.RCONPassword.Trim().Length <= 0)
            {
                dtcdc.RCONPassword = Utils.GetRandomString(16);
                developerToolsRCONPasswordSingleLineTextField.Text = dtcdc.RCONPassword;
            }
            SAMP.DeveloperToolsConfigIO = dtcdc;
        }

        /// <summary>
        /// Add selcted API
        /// </summary>
        private void AddSelectedAPI()
        {
            APIForm apif = new APIForm(null);
            DialogResult result = apif.ShowDialog();
            DialogResult = DialogResult.None;
            if (result == DialogResult.OK)
            {
                List<ServerListConnector> api = SAMP.APIIO;
                api.Add(new ServerListConnector(apif.API));
                SAMP.APIIO = api;
                ReloadAPIs();
            }
        }

        /// <summary>
        /// Edit selected API
        /// </summary>
        private void EditSelectedAPI()
        {
            foreach (DataGridViewRow dgvr in apiGridView.SelectedRows)
            {
                int index = (int)(dgvr.Cells[0].Value);
                if ((index >= 0) && (index < apis.Count))
                {
                    ServerListConnector slc = apis[index];
                    APIForm apif = new APIForm(slc.APIDataContract);
                    DialogResult result = apif.ShowDialog();
                    DialogResult = DialogResult.None;
                    if (result == DialogResult.OK)
                    {
                        apis[index] = new ServerListConnector(apif.API);
                        SAMP.APIIO = apis;
                        ReloadAPIs();
                    }
                }
                break;
            }
        }

        /// <summary>
        /// Remove selected API
        /// </summary>
        private void RemoveSelectedAPI()
        {
            if (MessageBox.Show(Translator.GetTranslation("REMOVE_API"), Translator.GetTranslation("REMOVE_API_TITLE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                List<ServerListConnector> rl = new List<ServerListConnector>();
                List<ServerListConnector> apis_result = SAMP.APIIO;
                foreach (DataGridViewRow dgvr in apiGridView.SelectedRows)
                {
                    int index = (int)(dgvr.Cells[0].Value);
                    if (apis_result.Count > index)
                    {
                        rl.Add(apis_result[index]);
                    }
                }
                foreach (ServerListConnector item in rl)
                {
                    apis_result.Remove(item);
                }
                SAMP.APIIO = apis_result;
                ReloadAPIs();
            }
        }

        /// <summary>
        /// Removeselection from favourites
        /// </summary>
        /// <param name="promptWhenError">Prompt when error</param>
        private void RemoveSelectionFromFavourites(bool promptWhenError = true)
        {
            if (IsInFavouriteList)
            {
                Server server = SelectedServer;
                if (server != null)
                {
                    if (MessageBox.Show(Translator.GetTranslation("REMOVE_SERVER_FROM_FAVOURITES"), Translator.GetTranslation("REMOVE_SERVER_FROM_FAVOURITES_TITLE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        Dictionary<string, Server> servers = apis[selectedAPIIndex].ServerListIO;
                        if (servers.ContainsKey(server.IPPortString))
                        {
                            servers.Remove(server.IPPortString);
                            apis[selectedAPIIndex].ServerListIO = servers;
                            ReloadFavourites(servers, selectedAPIIndex);
                        }
                    }
                }
            }
            else if (promptWhenError)
            {
                MessageBox.Show(Translator.GetTranslation("SERVER_NOT_IN_FAVOURITES"), Translator.GetTranslation("NOT_IN_FAVOURITES"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Multi-threaded lists timer tick event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void multithreadedListsTimer_Tick(object sender, EventArgs e)
        {
            lock (loadedServers)
            {
                foreach (KeyValuePair<Server, int> kv in loadedServers)
                {
                    DataRow dr = serversDataTable.NewRow();
                    object[] row = new object[7];
                    row[0] = kv.Value;
                    if (kv.Key.IsDataFetched(ERequestType.Ping))
                    {
                        row[1] = kv.Key.Ping;
                    }
                    else
                    {
                        row[1] = 1000U;
                    }
                    if (kv.Key.IsDataFetched(ERequestType.Information))
                    {
                        row[2] = kv.Key.Hostname;
                        row[3] = new PlayerCountString(kv.Key.PlayerCount, kv.Key.MaxPlayers);
                        row[4] = kv.Key.Gamemode;
                        row[5] = kv.Key.Language;
                    }
                    else
                    {
                        row[2] = "-";
                        row[3] = "0/0";
                        row[4] = "-";
                        row[5] = "-";
                    }
                    row[6] = kv.Key.IPPortString;
                    dr.ItemArray = row;
                    serversDataTable.Rows.Add(dr);
                    if (!(registeredServers.ContainsKey(kv.Key.IPPortString)))
                    {
                        registeredServers.Add(kv.Key.IPPortString, kv.Key);
                    }
                    if (queryFirstServer)
                    {
                        queryFirstServer = !(EnterRow());
                    }
                    lock (apis)
                    {
                        if ((kv.Value >= 0) && (kv.Value < apis.Count))
                        {
                            ++apis[kv.Value].ServerCount;
                        }
                    }
                }
                loadedServers.Clear();
                UpdateServerCount();
            }
            if (rowThreadSuccess)
            {
                rowThreadSuccess = false;
                ReloadSelectedServerRow();
            }

            lock (loadedGallery)
            {
                foreach (KeyValuePair<string, Image> kv in loadedGallery)
                {
                    galleryImageList.Images.Add(kv.Value);
                    ListViewItem lvi = galleryListView.Items.Add(kv.Key.Substring(SAMP.GalleryPath.Length + 1), (int)lastGalleryImageIndex);
                    lvi.Tag = kv.Key;
                    ++lastGalleryImageIndex;
                }
                loadedGallery.Clear();
            }
        }

        /// <summary>
        /// Servers grid view row enter event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Data grid view cell event arguments</param>
        private void serversGridView_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            EnterRow();
        }

        /// <summary>
        /// Form closed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Form closed event args</param>
        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            LauncherConfigDataContract lcdc = new LauncherConfigDataContract(selectedAPIIndex, developmentDirectorySingleLineTextField.Text, useDiscordRichPresenceCheckBox.Checked);
            SAMP.LauncherConfigIO = lcdc;
            SaveDeveloperToolsConfig();
            lock (loadServers)
            {
                foreach (KeyValuePair<Server, int> kv in loadServers)
                {
                    kv.Key.Dispose();
                }
                loadServers.Clear();
            }
            lock (loadedServers)
            {
                foreach (KeyValuePair<Server, int> kv in loadedServers)
                {
                    kv.Key.Dispose();
                }
                loadedServers.Clear();
            }
            lock (loadedGallery)
            {
                loadedGallery.Clear();
            }
            if (serversThread != null)
            {
                serversThread.Abort();
                serversThread = null;
            }
            if (rowThread != null)
            {
                rowThread.Abort();
                rowThread = null;
            }
            if (galleryThread != null)
            {
                galleryThread.Abort();
                galleryThread = null;
            }
            SAMP.CloseLastServer();
            ThumbnailsCache.Clear();
        }

        /// <summary>
        /// Languages combo box selected index changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void languagesComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int i = languagesComboBox.SelectedIndex;
            if (i >= 0)
            {
                List<Language> langs = new List<Language>(Translator.TranslatorInterface.Languages);
                if (Translator.ChangeLanguage(langs[i]))
                {
                    Application.Restart();
                }
            }
        }

        /// <summary>
        /// GitHub project picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void gitHubProjectPictureBox_Click(object sender, EventArgs e)
        {
            SAMP.OpenGitHubProject();
        }

        /// <summary>
        /// Generic grid view data error event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Data grid view data error event arguments</param>
        private void genericGridView_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            // null route
            e.ThrowException = false;
        }

        /// <summary>
        /// Filter single line text field text changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void filterSingleLineTextField_TextChanged(object sender, EventArgs e)
        {
            UpdateServerListFilter();
        }

        /// <summary>
        /// Filter generic radio button checked changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void filterGenericRadioButton_CheckedChanged(object sender, EventArgs e)
        {
            UpdateServerListFilter();
        }

        /// <summary>
        /// Generic picture box mouse enter event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void genericPictureBox_MouseEnter(object sender, EventArgs e)
        {
            Cursor = Cursors.Hand;
        }

        /// <summary>
        /// Generic picture box mouse leave event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void genericPictureBox_MouseLeave(object sender, EventArgs e)
        {
            Cursor = Cursors.Default;
        }

        /// <summary>
        /// Main tab control selected index changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void mainTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mainTabControl.SelectedTab == galleryPage)
            {
                if (galleryThread == null)
                {
                    ReloadGallery();
                }
            }
        }

        /// <summary>
        /// Gallery file system watcher changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">File system event arguments</param>
        private void galleryFileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine("galleryFileSystemWatcher_Changed begin");
            ThumbnailsCache.RemoveFromCache(e.FullPath);
            ReloadGallery();
            Debug.WriteLine("galleryFileSystemWatcher_Changed end");
        }

        /// <summary>
        /// Gallery file system watcher created event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">File system event arguments</param>
        private void galleryFileSystemWatcher_Created(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine("galleryFileSystemWatcher_Created begin");
            ReloadGallery();
            Debug.WriteLine("galleryFileSystemWatcher_Created end");
        }

        /// <summary>
        /// Gallery file system watcher Deleted event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">File system event arguments</param>
        private void galleryFileSystemWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine("galleryFileSystemWatcher_Deleted begin");
            ThumbnailsCache.RemoveFromCache(e.FullPath);
            ReloadGallery();
            Debug.WriteLine("galleryFileSystemWatcher_Deleted end");
        }

        /// <summary>
        /// Gallery file system watcher renamed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Renamed event arguments</param>
        private void galleryFileSystemWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            ThumbnailsCache.RemoveFromCache(e.OldFullPath);
            ReloadGallery();
        }

        /// <summary>
        /// Gallery view picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void galleryViewPictureBox_Click(object sender, EventArgs e)
        {
            ViewSelectedImage();
        }

        /// <summary>
        /// Gallery ddelete picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void galleryDeletePictureBox_Click(object sender, EventArgs e)
        {
            DeleteSelectedImage();
        }

        /// <summary>
        /// Gallery list view double click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void galleryListView_DoubleClick(object sender, EventArgs e)
        {
            ViewSelectedImage();
        }

        /// <summary>
        /// Show gallery picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void showGalleryPictureBox_Click(object sender, EventArgs e)
        {
            SAMP.ShowGallery();
        }

        /// <summary>
        /// Gallery list view key up event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Key event arguments</param>
        private void galleryListView_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                ViewSelectedImage();
            }
            else if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedImage();
            }
        }

        /// <summary>
        /// Connect button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void connectButton_Click(object sender, EventArgs e)
        {
            Connect(quitWhenDone: closeWhenLaunchedCheckBox.Checked);
        }

        /// <summary>
        /// Launch debug mode button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void launchDebugModeButton_Click(object sender, EventArgs e)
        {
            SAMP.LaunchSAMPDebugMode(closeWhenLaunchedCheckBox.Checked, this);
        }

        /// <summary>
        /// Launch singleplayer button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void launchSingleplayerButton_Click(object sender, EventArgs e)
        {
            SAMP.LaunchSingleplayerMode(closeWhenLaunchedCheckBox.Checked, this);
        }

        /// <summary>
        /// Text file system watcher changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">File system event arguments</param>
        private void textFileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            switch (e.Name)
            {
                case "chatlog.txt":
                    lastChatlogTextBox.Text = SAMP.Chatlog;
                    break;
                case "savedpositions.txt":
                    savedPositionsTextBox.Text = SAMP.SavedPositions;
                    break;
            }
        }

        /// <summary>
        /// Save configuration button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void saveConfigButton_Click(object sender, EventArgs e)
        {
            SaveConfig();
        }

        /// <summary>
        /// Page size single line text field text changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void pageSizeSingleLineTextField_TextChanged(object sender, EventArgs e)
        {
            int v = 0;
            if (!(int.TryParse(pageSizeSingleLineTextField.Text, out v)))
            {
                pageSizeSingleLineTextField.Text = "0";
            }
        }

        /// <summary>
        /// FPS limit track bar value changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void fpsLimitTrackBar_ValueChanged(object sender, EventArgs e)
        {
            fpsLimitSingleLineTextField.Text = fpsLimitTrackBar.Value.ToString();
        }

        /// <summary>
        /// FPS limit single line text field text changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void fpsLimitSingleLineTextField_TextChanged(object sender, EventArgs e)
        {
            int v = 0;
            if (int.TryParse(fpsLimitSingleLineTextField.Text, out v))
            {
                if (v < fpsLimitTrackBar.Minimum)
                {
                    v = fpsLimitTrackBar.Minimum;
                }
                else if (v > fpsLimitTrackBar.Maximum)
                {
                    v = fpsLimitTrackBar.Maximum;
                }
                if (fpsLimitSingleLineTextField.Text != v.ToString())
                {
                    fpsLimitSingleLineTextField.Text = v.ToString();
                }
                fpsLimitTrackBar.Value = v;
            }
            else
            {
                fpsLimitSingleLineTextField.Text = fpsLimitTrackBar.Minimum.ToString();
                fpsLimitTrackBar.Value = fpsLimitTrackBar.Minimum;
            }
        }

        /// <summary>
        /// Revert configuration button event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void revertConfigButton_Click(object sender, EventArgs e)
        {
            ReloadSAMPConfig();
        }

        /// <summary>
        /// Show extended server information tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void showExtendedServerInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Server server = SelectedServer;
            if (server != null)
            {
                multithreadedListsTimer.Stop();
                ExtendedServerInformationForm esif = new ExtendedServerInformationForm(server);
                esif.ShowDialog();
                DialogResult = DialogResult.None;
                multithreadedListsTimer.Start();
            }
        }

        /// <summary>
        /// Add server to favourites tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void addServerToFavouritesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Server server = SelectedServer;
            if (server != null)
            {
                List<IndexedServerListConnector> connectors = new List<IndexedServerListConnector>();
                for (int i = 0; i < apis.Count; i++)
                {
                    ServerListConnector slc = apis[i];
                    if ((slc.ServerListType == EServerListType.Favourites) || (slc.ServerListType == EServerListType.LegacyFavourites))
                    {
                        connectors.Add(new IndexedServerListConnector(slc, i));
                    }
                }
                if (connectors.Count <= 0)
                {
                    MessageBox.Show(Translator.GetTranslation("NO_FAVOURITES"), Translator.GetTranslation("NO_FAVOURITES_TITLE"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    IndexedServerListConnector islc = null;
                    if (connectors.Count > 1)
                    {
                        FavouriteListsForm flf = new FavouriteListsForm(connectors);
                        DialogResult result = flf.ShowDialog();
                        DialogResult = DialogResult.None;
                        if (result == DialogResult.OK)
                        {
                            islc = flf.SelectedServerListConnector;
                        }
                    }
                    else
                    {
                        islc = connectors[0];
                    }
                    if (islc != null)
                    {
                        ServerListConnector slc = islc.ServerListConnector;
                        Dictionary<string, Server> servers = slc.ServerListIO;
                        if (servers.ContainsKey(server.IPPortString))
                        {
                            MessageBox.Show(Translator.GetTranslation("SERVER_ALREADY_IN_FAVOURITES"), Translator.GetTranslation("ALREADY_IN_FAVOURITES"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        else
                        {
                            servers.Add(server.IPPortString, server.ToFavouriteServer(server.Hostname, server.Gamemode, "", ""));
                            slc.ServerListIO = servers;
                            ReloadFavourites(servers, islc.Index);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Remove server from favourites tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void removeServerFromFavouritesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RemoveSelectionFromFavourites();
        }

        /// <summary>
        /// Connect tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void connectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Connect(quitWhenDone: closeWhenLaunchedCheckBox.Checked);
        }

        /// <summary>
        /// Connect with RCON tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void connectWithRCONToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RCONPasswordForm rconf = new RCONPasswordForm();
            DialogResult result = rconf.ShowDialog();
            DialogResult = DialogResult.None;
            if (result == DialogResult.OK)
            {
                Connect(null, rconf.RCONPassword, closeWhenLaunchedCheckBox.Checked);
            }
        }

        /// <summary>
        /// Servers grid view cell double click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Data grid view cell event arguments</param>
        private void serversGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                Connect(quitWhenDone: closeWhenLaunchedCheckBox.Checked);
            }
        }

        /// <summary>
        /// Select API combo box selected index changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void selectAPIComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            selectedAPIIndex = selectAPIComboBox.SelectedIndex;
            SelectAPI();
        }

        /// <summary>
        /// GitHub link label link clicked event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Link label link clicked event arguments</param>
        private void gitHubLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            SAMP.OpenGitHubProject();
        }

        /// <summary>
        /// API add picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void apiAddPictureBox_Click(object sender, EventArgs e)
        {
            AddSelectedAPI();
        }

        /// <summary>
        /// API remove picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void apiRemovePictureBox_Click(object sender, EventArgs e)
        {
            RemoveSelectedAPI();
        }

        /// <summary>
        /// API edit picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void apiEditPictureBox_Click(object sender, EventArgs e)
        {
            EditSelectedAPI();
        }

        /// <summary>
        /// Add new API tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void addNewAPIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddSelectedAPI();
        }

        /// <summary>
        /// Edit selected API too strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void editSelectedAPIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EditSelectedAPI();
        }

        /// <summary>
        /// Remove selected API tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void removeSelectedAPIToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RemoveSelectedAPI();
        }

        /// <summary>
        /// API grid view double click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void apiGridView_DoubleClick(object sender, EventArgs e)
        {
            EditSelectedAPI();
        }

        /// <summary>
        /// Servers grid view key up event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Key event arguments</param>
        private void serversGridView_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectionFromFavourites(false);
            }
        }

        /// <summary>
        /// Revert API list tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void revertAPIListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(Translator.GetTranslation("REVERT_API_LIST"), Translator.GetTranslation("REVERT_API_LIST_TITLE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                SAMP.RevertAPIs();
                ReloadAPIs();
            }
        }

        /// <summary>
        /// Connect to host click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void connectToHostButton_Click(object sender, EventArgs e)
        {
            ConnectHostForm chf = new ConnectHostForm(SelectedServer);
            DialogResult result = chf.ShowDialog();
            DialogResult = DialogResult.None;
            if (result == DialogResult.OK)
            {
                Server server = new Server(chf.HostAndPort, false);
                Connect(server, quitWhenDone: closeWhenLaunchedCheckBox.Checked);
                server.Dispose();
            }
        }

        /// <summary>
        /// Rules grid view cell double click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Data grid view cell event arguments</param>
        private void rulesGridView_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                foreach (DataGridViewRow row in rulesGridView.SelectedRows)
                {
                    if (row.Cells[0].Value.ToString() == "weburl")
                    {
                        VisitWebsite(row.Cells[1].Value.ToString());
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Visit website tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void visitWebsiteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedServer != null)
            {
                VisitWebsite(SelectedServer.GetRuleValue("weburl"));
            }
        }

        /// <summary>
        /// Search on Bing tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void searchOnBingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedServer != null)
            {
                Process.Start("https://bing.com/search?q=" + Uri.EscapeUriString(SelectedServer.IPPortString + " " + SelectedServer.Hostname));
            }
        }

        /// <summary>
        /// Search on DuckDuckGo tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void searchOnDuckDuckGoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedServer != null)
            {
                Process.Start("https://duckduckgo.com/?q=" + Uri.EscapeUriString(SelectedServer.IPPortString + " " + SelectedServer.Hostname));
            }
        }

        /// <summary>
        /// Search on Google tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void searchOnGoogleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedServer != null)
            {
                Process.Start("https://google.com/search?q=" + Uri.EscapeUriString(SelectedServer.IPPortString + " " + SelectedServer.Hostname));
            }
        }

        /// <summary>
        /// Search on Yahoo tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void searchOnYahooToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedServer != null)
            {
                Process.Start("https://search.yahoo.com/search?p=" + Uri.EscapeUriString(SelectedServer.IPPortString + " " + SelectedServer.Hostname));
            }
        }

        /// <summary>
        /// Search on Yandex tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void searchOnYandexToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedServer != null)
            {
                Process.Start("https://yandex.ru/search/?text=" + Uri.EscapeUriString(SelectedServer.IPPortString + " " + SelectedServer.Hostname));
            }
        }

        /// <summary>
        /// Search on YouTube tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void searchOnYouTubeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (SelectedServer != null)
            {
                Process.Start("https://youtube.com/results?search_query=" + Uri.EscapeUriString(SelectedServer.IPPortString + " " + SelectedServer.Hostname));
            }
        }

        /// <summary>
        /// Developer tools show additional configurations button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void developerToolsShowAdditionalConfigurationsButton_Click(object sender, EventArgs e)
        {
            LauncherConfigDataContract lcdc = SAMP.LauncherConfigIO;
            lcdc.DevelopmentDirectory = developmentDirectorySingleLineTextField.Text;
            SAMP.LauncherConfigIO = lcdc;
            DeveloperToolsConfigDataContract dtcdc = SAMP.DeveloperToolsConfigIO;
            dtcdc.Hostname = developerToolsHostnameSingleLineTextField.Text;
            dtcdc.Port = Utils.GetInt(developerToolsPortSingleLineTextField.Text);
            dtcdc.Password = developerToolsServerPasswordSingleLineTextField.Text;
            dtcdc.RCONPassword = developerToolsRCONPasswordSingleLineTextField.Text;
            DeveloperToolsConfigForm dtcf = new DeveloperToolsConfigForm(dtcdc);
            DialogResult result = dtcf.ShowDialog();
            DialogResult = DialogResult.None;
            if (result == DialogResult.OK)
            {
                dtcdc = dtcf.DeveloperToolsConfigDataContract;
                developerToolsHostnameSingleLineTextField.Text = dtcdc.Hostname;
                developerToolsPortSingleLineTextField.Text = dtcdc.Port.ToString();
                developerToolsServerPasswordSingleLineTextField.Text = dtcdc.Password;
                developerToolsRCONPasswordSingleLineTextField.Text = dtcdc.RCONPassword;
                List<string> entries = new List<string>();
                foreach (var item in developerToolsGamemodesCheckedListBox.CheckedItems)
                {
                    entries.Add(item.ToString());
                }
                dtcdc.Gamemodes = entries.ToArray();
                entries.Clear();
                foreach (var item in developerToolsFilterscriptsCheckedListBox.CheckedItems)
                {
                    entries.Add(item.ToString());
                }
                dtcdc.Filterscripts = entries.ToArray();
                entries.Clear();
                foreach (var item in developerToolsPluginsCheckedListBox.CheckedItems)
                {
                    entries.Add(item.ToString());
                }
                dtcdc.Plugins = entries.ToArray();
                entries.Clear();
                SAMP.DeveloperToolsConfigIO = dtcdc;
                ReloadDeveloperToolsConfig();
            }
        }

        /// <summary>
        /// Development directory single line text field text changed event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void developmentDirectorySingleLineTextField_TextChanged(object sender, EventArgs e)
        {
            if (Directory.Exists(developmentDirectorySingleLineTextField.Text))
            {
                LauncherConfigDataContract lcdc = SAMP.LauncherConfigIO;
                lcdc.DevelopmentDirectory = developmentDirectorySingleLineTextField.Text;
                SAMP.LauncherConfigIO = lcdc;
                ReloadDeveloperToolsConfig();
            }
        }

        /// <summary>
        /// Developer tools open directory button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void developerToolsOpenDirectoryButton_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(developmentDirectorySingleLineTextField.Text))
            {
                Process.Start("explorer.exe", Path.GetFullPath(developmentDirectorySingleLineTextField.Text));
            }
        }

        /// <summary>
        /// Developer tools start server button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void developerToolsStartServerButton_Click(object sender, EventArgs e)
        {
            SaveDeveloperToolsConfig();
            if (SAMPCTLProvider.Update())
            {
                SAMP.LaunchSAMPServer();
            }
        }

        /// <summary>
        /// Stop server button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void stopServerButton_Click(object sender, EventArgs e)
        {
            SAMP.CloseLastServer();
        }

        /// <summary>
        /// Developer tools connect to test server button click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void developerToolsConnectToTestServerButton_Click(object sender, EventArgs e)
        {
            DeveloperToolsConfigDataContract dtcdc = SAMP.DeveloperToolsConfigIO;
            Connect(new Server("127.0.0.1:" + dtcdc.Port, false), dtcdc.RCONPassword);
        }

        /// <summary>
        /// Patch version picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void patchVersionPictureBox_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in versionsListView.SelectedItems)
            {
                if (item.Tag is SAMPVersionDataContract)
                {
                    SAMPVersionDataContract version = (SAMPVersionDataContract)(item.Tag);
                    if (SAMPProvider.CurrentVersion != version)
                    {
                        if (MessageBox.Show(string.Format(Translator.GetTranslation("PATCH_VERSION"), version.Name), Translator.GetTranslation("PATCH_VERSION_TITLE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        {
                            SAMP.ChangeSAMPVersion(version, false);
                            ReloadVersions();
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Install version picture box click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void installVersionPictureBox_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in versionsListView.SelectedItems)
            {
                if (item.Tag is SAMPVersionDataContract)
                {
                    SAMPVersionDataContract version = (SAMPVersionDataContract)(item.Tag);
                    if (SAMPProvider.CurrentVersion != version)
                    {
                        if (MessageBox.Show(string.Format(Translator.GetTranslation("INSTALL_VERSION"), version.Name), Translator.GetTranslation("INSTALL_VERSION_TITLE"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        {
                            SAMP.ChangeSAMPVersion(version, true);
                            ReloadVersions();
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Versions list view mouse double click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Mouse event arguments</param>
        private void versionsListView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            foreach (ListViewItem item in versionsListView.SelectedItems)
            {
                if (item.Tag is SAMPVersionDataContract)
                {
                    SAMPVersionDataContract version = (SAMPVersionDataContract)(item.Tag);
                    if (SAMPProvider.CurrentVersion != version)
                    {
                        switch (MessageBox.Show(string.Format(Translator.GetTranslation("SELECT_VERSION"), version.Name), Translator.GetTranslation("SELECT_VERSION_TITLE"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                        {
                            case DialogResult.Yes:
                                SAMP.ChangeSAMPVersion(version, false);
                                ReloadVersions();
                                break;
                            case DialogResult.No:
                                SAMP.ChangeSAMPVersion(version, true);
                                ReloadVersions();
                                break;
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Add address to favourites list tool strip menu item click event
        /// </summary>
        /// <param name="sender">Sender</param>
        /// <param name="e">Event arguments</param>
        private void addAddressToFavouriteListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AddAddressToFavouriteListForm aatflf = new AddAddressToFavouriteListForm(FavouriteLists);
            DialogResult result = aatflf.ShowDialog();
            DialogResult = DialogResult.None;
            if (result == DialogResult.OK)
            {
                IndexedServerListConnector islc = aatflf.SelectedServerListConnector;
                ServerListConnector slc = islc.ServerListConnector;
                Dictionary<string, Server> servers = slc.ServerListIO;
                FavouriteServer server = new FavouriteServer(aatflf.Address, "", "", "", "");
                if (servers.ContainsKey(server.IPPortString))
                {
                    MessageBox.Show(Translator.GetTranslation("SERVER_ALREADY_IN_FAVOURITES"), Translator.GetTranslation("ALREADY_IN_FAVOURITES"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    servers.Add(server.IPPortString, server);
                    slc.ServerListIO = servers;
                    ReloadFavourites(servers, islc.Index);
                }
                server.Dispose();
            }
        }
    }
}
