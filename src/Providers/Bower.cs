﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EnvDTE;
using Newtonsoft.Json.Linq;

namespace PackageInstaller
{
    class Bower : BasePackageProvider
    {
        private static bool _isDownloading;
        private static ImageSource _icon = BitmapFrame.Create(new Uri("pack://application:,,,/PackageInstaller;component/Resources/bower.png", UriKind.RelativeOrAbsolute));
        private static Dictionary<string, IEnumerable<string>> _versions = new Dictionary<string, IEnumerable<string>>();

        public override string Name
        {
            get { return "Bower"; }
        }

        public override ImageSource Icon
        {
            get { return _icon; }
        }

        public override string DefaultArguments
        {
            get { return VSPackage.Settings.BowerArguments; }
        }

        public override async Task<IEnumerable<string>> GetPackagesInternal(string term = null)
        {
            string file = Path.Combine(Path.GetTempPath(), "bower-registry.txt");
            string url = "https://bower-component-list.herokuapp.com/";

            return await UpdateFileCache(file, url);
        }

        private static Regex _regex = new Regex(@"([\s]+)- (?<version>(\d)([\S]+))", RegexOptions.Compiled);

        public async override Task<IEnumerable<string>> GetVersionInternal(string packageName)
        {
            if (_versions.ContainsKey(packageName))
                return _versions[packageName];

            var start = new System.Diagnostics.ProcessStartInfo("cmd", "/c bower info " + packageName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ErrorDialog = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            ModifyPathVariable(start);
            List<string> list = new List<string>();

            using (var p = System.Diagnostics.Process.Start(start))
            {
                string output = await p.StandardOutput.ReadToEndAsync();
                p.WaitForExit();

                foreach (Match match in _regex.Matches(output))
                {
                    string version = match.Groups["version"].Value;

                    if (!list.Contains(version) && !version.Contains('+'))
                        list.Add(version);
                }
            }

            _versions.Add(packageName, list);

            return list;
        }

        public override async Task<bool> InstallPackage(Project project, string packageName, string version, string args = null)
        {
            string installArgs = GetInstallArguments(packageName, version);

            string arg = $"/c {installArgs} --no-color {args}";
            string cwd = project.GetRootFolder();
            string json = Path.Combine(cwd, "bower.json");

            if (!File.Exists(json))
            {
                string content = "{\"name\":\"myproject\"}";
                File.WriteAllText(json, content, new UTF8Encoding(false));
                project.ProjectItems.AddFromFile(json);
            }

            return await CallCommand(arg, cwd);
        }

        public override string GetInstallArguments(string name, string version)
        {
            string args = $"bower install {name}";

            if (!string.IsNullOrEmpty(version))
                args = $"{args}#{version}";

            return args;
        }

        private static async Task<IEnumerable<string>> UpdateFileCache(string file, string url)
        {
            if (!File.Exists(file))
            {
                using (var client = new WebClient())
                {
                    string json = await client.DownloadStringTaskAsync(url);
                    var list = ToList(json);
                    File.WriteAllLines(file, list);

                    return list;
                }
            }

            if (!_isDownloading && File.GetLastWriteTime(file) < DateTime.Now.AddDays(-1))
            {
                _isDownloading = true;

                System.Threading.ThreadPool.QueueUserWorkItem((o) =>
                {
                    try
                    {
                        using (var client = new WebClient())
                        {
                            string json = client.DownloadString(url);
                            var list = ToList(json);
                            File.WriteAllLines(file, list);
                        }
                    }
                    catch (Exception) { }

                    _isDownloading = false;
                });
            }

            return await Task.Run(() => File.ReadAllLines(file));
        }

        private static IEnumerable<string> ToList(string json)
        {
            var array = JArray.Parse(json);

            var names = from obj in array
                        let children = obj.Children<JProperty>()
                        let name = children.First(prop => prop.Name == "name").Value.ToString()
                        let stars = int.Parse(children.First(prop => prop.Name == "stars").Value.ToString())
                        let updated = DateTime.Parse(children.First(prop => prop.Name == "updated").Value.ToString())
                        where stars > 3 && updated > DateTime.Now.AddMonths(-6)
                        select name;

            return names.OrderBy(name => name, new PackageNameComparer());
        }
    }
}
