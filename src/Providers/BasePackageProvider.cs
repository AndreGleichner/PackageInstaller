﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using EnvDTE;

namespace PackageInstaller
{
    public abstract class BasePackageProvider : IPackageProvider
    {
        public abstract string Name { get; }

        public abstract ImageSource Icon { get; }

        public abstract string DefaultArguments { get; }

        public abstract Task<IEnumerable<string>> GetPackages(string term = null);

        public abstract Task<bool> InstallPackage(Project project, string packageName, string version, string args = null);

        public abstract Task<IEnumerable<string>> GetVersion(string packageName);

        public virtual bool EnableDynamicSearch { get { return false; } }

        protected virtual async Task<bool> CallCommand(string argument, string cwd)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                WorkingDirectory = cwd,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                FileName = "cmd.exe",
                Arguments = argument,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };

            ModifyPathVariable(start);

            try
            {
                using (var p = System.Diagnostics.Process.Start(start))
                {
                    var error = await p.StandardError.ReadToEndAsync();
                    var output = await p.StandardOutput.ReadToEndAsync();
                    p.WaitForExit();

                    Logger.Log(output, true);

                    if (p.ExitCode == 0)
                    {
                        VSPackage.UpdateStatus("Package installed");
                    }
                    else
                    {
                        VSPackage.UpdateStatus("An error installing package. See output window for details");
                        Logger.Log(error, true);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                VSPackage.UpdateStatus("An error installing package. See output window for details");
                Logger.Log(ex, true);
                return false;
            }
        }

        protected static void ModifyPathVariable(ProcessStartInfo start)
        {
            string path = ".\\node_modules\\.bin" + ";" + start.EnvironmentVariables["PATH"];

            string toolsDir = Environment.GetEnvironmentVariable("VS140COMNTOOLS");

            if (Directory.Exists(toolsDir))
            {
                string parent = Directory.GetParent(toolsDir).Parent.FullName;
                path += ";" + Path.Combine(parent, @"IDE\Extensions\Microsoft\Web Tools\External");
                path += ";" + Path.Combine(parent, @"IDE\Extensions\Microsoft\Web Tools\External\git");
            }

            start.EnvironmentVariables["PATH"] = path;
        }

        public override string ToString()
        {
            return Name;
        }

        public virtual string GetInstallArguments(string name, string version)
        {
            return null;
        }
    }
}
