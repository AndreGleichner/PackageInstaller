﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;

namespace PackageInstaller
{
    public partial class InstallDialog : Window
    {
        private SettingsManager _settings;
        private IEnumerable<IPackageProvider> _providers;
        private string _lastSearch;
        private const string LATEST = "Latest version";
        private const string LOADING = "Loading...";
        private const string NO_ARGS = "<args>";
        private static string[] _tips = new[] {
            "Tip: Type 'b:' to select Bower from dropdown",
            "Tip: Type 'j:' to select JSPM from dropdown",
            "Tip: Type 'nu:' to select NuGet from dropdown",
            "Tip: Type 'n:' to select npm from dropdown",
            "Tip: Type 't:' to select TSD from dropdown",
        };

        public InstallDialog(IServiceProvider serviceProvider, params IPackageProvider[] providers)
        {
            InitializeComponent();

            _providers = providers;

            Loaded += (s, e) =>
            {
                _settings = new ShellSettingsManager(serviceProvider);

                Icon = BitmapFrame.Create(new Uri("pack://application:,,,/PackageInstaller;component/Resources/dialog-icon.png", UriKind.RelativeOrAbsolute));
                Closing += StoreLastUsed;
                cbName.Focus();

                cbVersion.ItemsSource = new[] { LATEST };
                cbVersion.GotFocus += VersionFocus;

                cbType.ItemsSource = _providers;
                cbType.SelectionChanged += TypeChanged;
                txtArguments.GotFocus += delegate { Dispatcher.BeginInvoke(new Action(() => { txtArguments.SelectAll(); }), DispatcherPriority.SystemIdle, null); };

                string lastUsed = GetLastUsed();

                if (string.IsNullOrEmpty(lastUsed))
                {
                    cbType.SelectedIndex = 0;
                }
                else
                {
                    var provider = providers.FirstOrDefault(p => p.Name.Equals(lastUsed, StringComparison.OrdinalIgnoreCase));
                    if (provider != null)
                        cbType.SelectedItem = provider;
                }

                SetRandomTip();
                SetInstallArguments();
            };
        }

        private void SetRandomTip()
        {
            Random rdn = new Random(DateTime.Now.GetHashCode());
            lblTip.Content = _tips[rdn.Next(_tips.Length)];
        }

        private async void VersionFocus(object sender, EventArgs e)
        {
            if (_lastSearch == Provider.Name + cbName.Text)
                return;

            _lastSearch = Provider.Name + cbName.Text;

            cbVersion.ItemsSource = new[] { LOADING };
            cbVersion.SelectedIndex = 0;

            var versions = await Provider.GetVersion(cbName.Text.Trim());

            if (versions.Any())
                cbVersion.ItemsSource = versions;
            else
                cbVersion.ItemsSource = new[] { LATEST };

            if (string.IsNullOrWhiteSpace(cbVersion.Text))
                cbVersion.SelectedIndex = 0;

            SetInstallArguments();
        }

        private void TypeChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBox box = (ComboBox)sender;
            IPackageProvider provider = (IPackageProvider)box.SelectedItem;

            cbName.ItemsSource = null;
            txtArguments.Text = NO_ARGS;

            SetRandomTip();
            GetPackages();

            txtArguments.Text = string.IsNullOrWhiteSpace(Provider?.DefaultArguments) ? NO_ARGS : Provider.DefaultArguments;
            SetInstallArguments();
        }

        public string Package
        {
            get { return cbName.Text; }
        }

        public string Arguments
        {
            get { return txtArguments.Text.Replace(NO_ARGS, string.Empty).Trim(); }
        }

        public IPackageProvider Provider
        {
            get { return (IPackageProvider)cbType.SelectedItem; }
        }

        public string Version
        {
            get
            {
                if (cbVersion.Text == LATEST || cbVersion.Text == LOADING || string.IsNullOrWhiteSpace(cbVersion.Text))
                    return null;

                return cbVersion.Text;
            }
        }

        private async void GetPackages()
        {
            IEnumerable<string> source;

            if (cbName.ItemsSource != null)
            {
                var current = (IEnumerable<string>)cbName.ItemsSource;
                source = current.Union(await Provider.GetPackages(cbName.Text)).OrderBy(s => s);
            }
            else
            {
                source = await Provider.GetPackages(cbName.Text);
            }

            cbName.ItemsSource = source;
        }

        private void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void cbName_TextChanged(object sender, RoutedEventArgs e)
        {
            cbVersion.IsEnabled = !string.IsNullOrWhiteSpace(cbName.Text);

            if (cbName.Text.EndsWith(":", StringComparison.Ordinal))
            {
                string providerMatch = cbName.Text.TrimEnd(':');

                foreach (var provider in _providers)
                {
                    if (!provider.Name.StartsWith(providerMatch, StringComparison.OrdinalIgnoreCase))
                        continue;

                    cbName.Text = string.Empty;
                    cbType.SelectedItem = provider;
                    SetRandomTip();
                    return;
                }
            }

            cbVersion.ItemsSource = new[] { LATEST };
            cbVersion.SelectedIndex = 0;

            if (!cbVersion.IsEnabled)
                cbVersion.Text = LATEST;

            if (Provider.EnableDynamicSearch)
                GetPackages();

            SetInstallArguments();
        }

        private void cbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SetInstallArguments();
        }

        private void SetInstallArguments()
        {
            lblCommand.Visibility = Visibility.Hidden;
            txtArguments.Visibility = Visibility.Hidden;

            if (Provider == null)
                return;

            if (!string.IsNullOrEmpty(Package))
            {
                string installArgs = Provider.GetInstallArguments(Package, Version);

                if (!string.IsNullOrEmpty(installArgs))
                {
                    lblCommand.Content = installArgs;
                    lblCommand.Visibility = Visibility.Visible;
                    txtArguments.Visibility = Visibility.Visible;
                }
            }
        }

        private string GetLastUsed()
        {
            try
            {
                SettingsStore store = _settings.GetReadOnlySettingsStore(SettingsScope.UserSettings);
                return store.GetString("PackageInstaller", "type", null);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return null;
            }
        }

        private void StoreLastUsed(object sender, EventArgs e)
        {
            Closing -= StoreLastUsed;

            try
            {
                WritableSettingsStore wstore = _settings.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!wstore.CollectionExists("PackageInstaller"))
                    wstore.CreateCollection("PackageInstaller");

                wstore.SetString("PackageInstaller", "type", cbType.Text);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }
    }
}
