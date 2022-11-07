using ComputerUtils.Logging;
using ComputerUtils.VarUtils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MasterServer
{
    public class OtherServer
    {
        public string name { get; set; } = "";
        public string dll { get; set; } = "";
        public string folder { get
            {
                string f = Path.GetDirectoryName(dll);
                if (!f.EndsWith(Path.DirectorySeparatorChar)) f += Path.DirectorySeparatorChar;
                return f;
            } }
        public Process process;
        public Thread logThread = null;
        public string log = "";
        public int maxLogLength { get; set; } = 50000;
        public bool shouldRestartInInterval { get; set; } = false;
        public int restartIntervalInSeconds { get; set; } = 60 * 60 * 24;
        public long ramUsage { get; set; } = 0;
        public string ramUsageString { get { return SizeConverter.ByteSizeToString(ramUsage); } }
        public bool shouldRestartIfMoreRam { get; set; } = false;
        public string status { get; set; } = "Starting up";
        public int restartMaxRam { get; set; } = 200 * 1024 * 1024;
        public DateTime fakeLastStartTime { get { return lastStartTime; } }
        public DateTime lastStartTime = DateTime.MinValue;
        public int notRunningTimes = 0;
        int loggingThreadsStarted = 0;

        public void Start(bool force = false)
        {
            if (!File.Exists(dll)) return;
            if(lastStartTime + TimeSpan.FromSeconds(40) > DateTime.Now && !force)
            {
                Logger.Log("Will not start " + name + " as it's already been attempted to start less than 40 seconds ago", LoggingType.Warning);
                return;
            }
            status = "Starting up";
            loggingThreadsStarted++;
            lastStartTime = DateTime.Now;
            if(process != null) Logger.Log(name + " is " + (process.HasExited ? "not " : "") + " running", LoggingType.Debug);
            ProcessStartInfo i = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "\"" + dll + "\"",
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"
            };
            Logger.Log("Starting " + i.FileName + " " + i.Arguments, LoggingType.Important);
            process = Process.Start(i);
            logThread = new Thread(() =>
            {
                int loggingThread = loggingThreadsStarted;
                Logger.Log("Logging thread for " + name + " started", LoggingType.Important);
                while (!process.HasExited && loggingThreadsStarted == loggingThread)
                {
                    try
                    {

                        log += process.StandardOutput.ReadLine() + "\n";
                        if (log.Length >= maxLogLength) log = log.Substring(log.Length - maxLogLength, maxLogLength);
                    } catch(Exception e)
                    {
                        Logger.Log("Error while reading Output of " + name + ", restarting server: " + e.ToString(), LoggingType.Error);
                        Server.SendMasterWebhookMessage("Error while reading output", "Error while reading Output of " + name + ", restarting server", 0xFFFF00);
                        Restart();
                    }
                }
            });
            UpdateStatus();
            logThread.Start();
        }

        public void UpdateStatus()
        {
            if (process == null)
            {
                status = "Process is null";
                return;
            }
            status = process.HasExited ? "Terminated" : "Running";
            ramUsage = GetRamUsage();
        }

        public long GetRamUsage()
        {
            Process process;
            try
            {
                process = Process.GetProcessById(this.process.Id);
            } catch(Exception e)
            {
                Logger.Log("Could not get ram usage as the server is not running. This should be fixed automatically", LoggingType.Warning);
                return 0;
            }
            /*
            Logger.Log("Working " + SizeConverter.ByteSizeToString(process.WorkingSet64));
            Logger.Log("Private " + SizeConverter.ByteSizeToString(process.PrivateMemorySize64));
            Logger.Log("Paged " + SizeConverter.ByteSizeToString(process.PagedMemorySize64));
            Logger.Log("PagedSystem " + SizeConverter.ByteSizeToString(process.PagedSystemMemorySize64));
            Logger.Log("NonPaged " + SizeConverter.ByteSizeToString(process.NonpagedSystemMemorySize64));
            Logger.Log("Virtual " + SizeConverter.ByteSizeToString(process.VirtualMemorySize64));
            */
            return process.HasExited ? 0 : process.WorkingSet64;
        }

        public void Restart()
        {
            Logger.Log("Restarting server " + name, LoggingType.Important);
            Kill();
            Start();
        }

        public void Kill()
        {
            if (process == null || process.HasExited)
            {
                Logger.Log("Cannot kill server cause " + (process == null ? "Process is null" : "Process has exited"), LoggingType.Warning);
                return;
            }
            Logger.Log("Killing server " + name, LoggingType.Important);
            process.Kill();
        }

        public string RequestAndResetCurrentLog()
        {
            string l = log;
            log = "";
            return l;
        }

        internal void UpdateServer(byte[] updateZip)
        {
            Kill();
            Logger.Log("Updating OculusDB", LoggingType.Important);
            string updateZipName = DateTime.Now.Ticks + ".zip";
            File.WriteAllBytes(updateZipName, updateZip);
            using (ZipArchive archive = ZipFile.OpenRead(updateZipName))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    String name = entry.FullName;
                    if (name.EndsWith("/")) continue;
                    if (name.Contains("/")) Directory.CreateDirectory(folder + Path.GetDirectoryName(name));
                    entry.ExtractToFile(folder + entry.FullName, true);
                    Logger.Log("Extracting " + name + " to " + folder + entry.FullName);
                }
            }
            File.Delete(updateZipName);
        }
    }
}
