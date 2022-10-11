using ComputerUtils.Logging;
using ComputerUtils.VarUtils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                return Path.GetDirectoryName(dll);
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
        public DateTime lastStartTime = DateTime.Now;
        public int notRunningTimes = 0;
        bool blockLogging = false;

        public void Start()
        {
            if (!File.Exists(dll)) return;
            status = "Starting up";
            blockLogging = true;
            lastStartTime = DateTime.Now;
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
                Logger.Log("Logging thread for " + name + " started", LoggingType.Important);
                while (!process.HasExited && !blockLogging)
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
            blockLogging = false;
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
            Logger.Log("Killing server " + name, LoggingType.Important);
            if (process == null || process.HasExited) return;
            process.Kill();
        }

        public string RequestAndResetCurrentLog()
        {
            string l = log;
            log = "";
            return l;
        }
    }
}
