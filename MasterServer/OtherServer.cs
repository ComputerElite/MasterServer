﻿using ComputerUtils.Logging;
using ComputerUtils.VarUtils;
using ComputerUtils.Webserver;
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
        public Process? process;
        public string serverToken { get; set; } = ""; // Allows for server restarts. Intended to only be used by the server which is running this server
        public Thread logThread = null;
        public string log = "";
        public SocketServerRequest logClient = null;
        public bool enabled { get; set; } = true;
        public int maxLogLength { get; set; } = 50000;
        public bool shouldRestartInInterval { get; set; } = false;
        public int restartIntervalInSeconds { get; set; } = 60 * 60 * 24;
        public long ramUsage { get; set; } = 0;
        public string ramUsageString { get { return SizeConverter.ByteSizeToString(ramUsage); } }
		public string currentCommit { get
            {
                if (File.Exists(folder + "commit.txt")) return File.ReadAllText(folder + "commit.txt");
                return "unknown";
            } }
		public bool shouldRestartIfMoreRam { get; set; } = false;
        public string status { get; set; } = "Starting up";
        public int restartMaxRam { get; set; } = 200 * 1024 * 1024;
        public DateTime fakeLastStartTime { get { return lastStartTime; } }
        public DateTime lastStartTime = DateTime.MinValue;
        public int notRunningTimes = 0;
        int loggingThreadsStarted = 0;

        /// <summary>
        /// Starts a server
        /// </summary>
        /// <param name="force">Whether to skip the check if the server has already been started or not</param>
        public void Start(bool force = false)
        {
            if (!enabled) return;
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
                        if(logClient != null && !logClient.handler.closed)
                        {
                            logClient.SendString(RequestAndResetCurrentLog());
                        } else if(logClient != null && logClient.handler.closed)
                        {
                            logClient = null;
                        }
                        if (log.Length >= maxLogLength) log = log.Substring(log.Length - maxLogLength, maxLogLength);
                    } catch(Exception e)
                    {
                        Logger.Log("Error while reading Output of " + name + ", restarting server: " + e.ToString(), LoggingType.Error);
                        Server.SendMasterWebhookMessage("Error while reading output", "Error while reading Output of " + name + ", restarting server", 0xFFFF00);
                        Restart();
                    }
                }
                Logger.Log("Process exited");
                string error = "";
                try
                {
                    error = process.StandardError.ReadToEnd();
                }
                catch (Exception e)
                {
                    Logger.Log("Error while capturing standard error: " + e);
                }
                try
                {
                    log += process.StandardOutput.ReadToEnd();
                }
                catch (Exception e)
                {
                    Logger.Log("Error while capturing standard output: " + e);
                }
                File.WriteAllText(Env.dataDir + name + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".log", "StandardError:\n\n" + error + "\n\n\nStandardOutput:\n\n" + log);
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
            if (!enabled) return;
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
            process.Kill(true); // Kill all child processes too
            process.WaitForExit(10000); // Wait up to 10 seconds for the process to exit
            Thread.Sleep(100); // Wait 100 ms for good measure
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
