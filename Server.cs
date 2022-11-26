using ComputerUtils.Discord;
using ComputerUtils.Logging;
using ComputerUtils.Updating;
using ComputerUtils.VarUtils;
using ComputerUtils.Webserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MasterServer
{
    public class Server
    {
        public HttpServer server;
        public static Config config { get { return Env.config; } }
        public static List<OtherServer> servers { get { return config.serversToWatch; } }
        public static Thread watchThread = null;

        public string GetToken(ServerRequest request, bool send403 = true)
        {
            Cookie token = request.cookies["token"];
            if (token == null)
            {
                if (send403) request.Send403();
                return "";
            }
            return token.Value;
        }

        public bool IsUserAdmin(ServerRequest request, bool send403 = true)
        {
            return GetToken(request, send403) == config.masterToken;
        }

        public void SetUpServers()
        {
            Logger.Log("Setting up servers from Config");
            for(int i = 0; i < servers.Count; i++)
            {
                Logger.Log("Starting up " + servers[i].name);
                servers[i].Start();
            }
        }

        public int GetServerIndex(string name)
        {
            for(int i = 0; i < servers.Count; i++)
            {
                if (servers[i].name.ToLower() == name.ToLower()) return i;
            }
            return -1;
        }

        public static void SendMasterWebhookMessage(string title, string message, int color)
        {
            if (config.masterWebhookUrl == "") return;
            try
            {
                Logger.Log("Sending master webhook");
                DiscordWebhook webhook = new DiscordWebhook(config.masterWebhookUrl);
                webhook.SendEmbed(title, message, "master " + DateTime.UtcNow, "Master Server", "https://computerelite.github.io/assets/CE_512px.png", "", "https://computerelite.github.io/assets/CE_512px.png", "", color);
            }
            catch (Exception ex)
            {
                Logger.Log("Exception while sending webhook" + ex.ToString(), LoggingType.Warning);
            }
        }

        public void WatchServers()
        {
            while(true)
            {
                Thread.Sleep(30000);
                for (int i = 0; i < servers.Count; i++)
                {
                    servers[i].UpdateStatus();
                    if (servers[i].process == null)
                    {
                        Logger.Log(servers[i].name + "s Process is null. Cannot check status", LoggingType.Error);
                        servers[i].notRunningTimes++;
                        if (servers[i].notRunningTimes > 1)
                        {
                            SendMasterWebhookMessage("Server not running!", servers[i].name + " is still not running. I'll try to restart it", 0xFFFF00);
                            servers[i].Restart();
                            continue;
                        }
                        SendMasterWebhookMessage("Process null", servers[i].name + "s Process is null. Cannot check status", 0xFF0000);
                        continue;
                    }
                    if (servers[i].process.HasExited)
                    {
                        Logger.Log(servers[i].name + " is not running anymore!!!!", LoggingType.Warning);
                        servers[i].notRunningTimes++;
                        if (servers[i].notRunningTimes > 1)
                        {
                            SendMasterWebhookMessage("Server not running!", servers[i].name + " is still not running. I'll try to restart it", 0xFFFF00);
                            servers[i].Restart();
                            continue;
                        }
                        SendMasterWebhookMessage("Server not running!", servers[i].name + " is not running anymore!!!!", 0xFF0000);
                    } else
                    {
                        if (servers[i].notRunningTimes > 0)
                        {
                            SendMasterWebhookMessage("Server running again", servers[i].name + " is running again", 0x00FF00);
                        }
                        servers[i].notRunningTimes = 0;
                        if (servers[i].shouldRestartIfMoreRam)
                        {
                            long ram = servers[i].GetRamUsage();
                            Logger.Log(servers[i].name + " is using " + SizeConverter.ByteSizeToString(ram) + " of ram");
                            if(ram > servers[i].restartMaxRam)
                            {
                                string message = "Restarting " + servers[i].name + " as it uses " + SizeConverter.ByteSizeToString(ram) + " ram of allowed " + SizeConverter.ByteSizeToString(servers[i].restartMaxRam);
                                Logger.Log(message, LoggingType.Important);
                                SendMasterWebhookMessage("Restarting Server", message, 0x00FF00);
                                servers[i].Restart();
                                continue;
                            }
                        }
                        if (servers[i].shouldRestartInInterval)
                        {
                            if (servers[i].fakeLastStartTime + new TimeSpan(0, 0, servers[i].restartIntervalInSeconds) < DateTime.Now)
                            {
                                string message = "Restarting " + servers[i].name + " as the period of " + SizeConverter.SecondsToBetterString(servers[i].restartIntervalInSeconds) + " has passed";
                                Logger.Log(message, LoggingType.Important);
                                SendMasterWebhookMessage("Restarting server", message, 0x00FF00);
                                servers[i].Restart();
                                continue;
                            }
                        }
                    }
                }
            }
        }

        public void StartServer()
        {
            SetUpServers();
            server = new HttpServer();
            Func<ServerRequest, bool> accessCheck = new Func<ServerRequest, bool>(request => IsUserAdmin(request, false));
            string frontend = "frontend" + Path.DirectorySeparatorChar;

            watchThread = new Thread(() =>
            {
                WatchServers();
            });
            watchThread.Start();

            // update master server
            server.AddRoute("POST", "/api/updatemasterserver/", new Func<ServerRequest, bool>(request =>
            {
                if (!IsUserAdmin(request)) return true;
                SendMasterWebhookMessage("Update deploying", "`" + request.queryString.Get("changelog") + "`", 0x42BBEB);
                request.SendString("Starting update");
                foreach(OtherServer s in servers)
                {
                    Logger.Log("Killing " + s.name);
                    s.Kill();
                } 
                Updater.StartUpdateNetApp(request.bodyBytes, Path.GetFileName(Assembly.GetExecutingAssembly().Location), Env.workingDir);
                return true;
            }));
            // update other server
            server.AddRoute("POST", "/api/updateserver/", new Func<ServerRequest, bool>(request =>
            {
                if (!IsUserAdmin(request)) return true;
                int i = GetServerIndex(request.queryString.Get("server"));
                SendMasterWebhookMessage("Update for " + servers[i].name + " deploying", "`" + request.queryString.Get("changelog") + "`", 0x42BBEB);
                request.SendString("Starting update");
                servers[i].UpdateServer(request.bodyBytes);
                servers[i].Start(true);
                return true;
            }));
            // kill server
            server.AddRoute("POST", "/api/kill/", new Func<ServerRequest, bool>(request =>
            {
                if (!IsUserAdmin(request)) return true;
                int index = GetServerIndex(request.pathDiff);
                if (index == -1)
                {
                    request.SendString("A server with this name does not exist", "text/plain", 400);
                    return true;
                }
                servers[index].Kill();
                request.SendString("Killed " + servers[index].name);
                return true;
            }), true);
            // restart server
            server.AddRoute("POST", "/api/restart/", new Func<ServerRequest, bool>(request =>
            {
                if (!IsUserAdmin(request)) return true;
                int index = GetServerIndex(request.pathDiff);
                if (index == -1)
                {
                    request.SendString("A server with this name does not exist", "text/plain", 400);
                    return true;
                }
                servers[index].Restart();
                request.SendString("Restarted " + servers[index].name);
                return true;
            }), true);
            // get application log
            server.AddRoute("GET", "/api/log/", new Func<ServerRequest, bool>(request =>
            {
                if (!IsUserAdmin(request)) return true;
                int index = GetServerIndex(request.pathDiff);
                if (index == -1)
                {
                    request.SendString("A server with this name does not exist", "text/plain", 400);
                    return true;
                }
                request.SendString(servers[index].RequestAndResetCurrentLog());
                return true;
            }), true);
            // get servers
            server.AddRoute("GET", "/api/servers/", new Func<ServerRequest, bool>(request =>
            {
                if (!IsUserAdmin(request)) return true;
                for(int i = 0; i < servers.Count; i++)
                {
                    config.serversToWatch[i].UpdateStatus();
                }
                request.SendString(JsonSerializer.Serialize(servers));
                return true;
            }));
            // set servers
            server.AddRoute("POST", "/api/servers/", new Func<ServerRequest, bool>(request =>
            {
                if (!IsUserAdmin(request)) return true;
                List<OtherServer> s = config.serversToWatch;
                config.serversToWatch = JsonSerializer.Deserialize<List<OtherServer>>(request.bodyString);
                for(int i = 0; i < servers.Count; i++)
                {
                    if (s.Any(x => x.name == servers[i].name))
                    {
                        config.serversToWatch[i].process = s[i].process;
                        config.serversToWatch[i].log = s[i].log;
                        config.serversToWatch[i].logThread = s[i].logThread;
                        config.serversToWatch[i].lastStartTime = s[i].lastStartTime;
                    }
					if (config.serversToWatch[i].dll == "") config.serversToWatch[i].dll = "/home/ComputerElite/test.dll";
				}
                foreach(OtherServer x in config.serversToWatch)
                {
                    if(!File.Exists(x.dll) && x.dll != "")
                    {
                        request.SendString("this dll does not exist!", "text/plain", 400);
                        config.serversToWatch = s;
                        return true;
                    }
                }
                config.Save();
                request.SendString("Saved Servers");
                return true;
            }));
            server.AddRoute("GET", "/api/user", new Func<ServerRequest, bool>(request =>
            {
                try
                {
                    string token = request.queryString.Get("token") ?? "";
                    LoginResponse response = new LoginResponse();
                    if (token != config.masterToken)
                    {
                        response.status = "This user does not exist";
                        request.SendString(JsonSerializer.Serialize(response), "application/json");
                        return true;
                    }
                    response.username = "admin";
                    response.redirect = "/admin";
                    response.token = token;
                    response.authorized = true;
                    request.SendString(JsonSerializer.Serialize(response), "application/json");
                }
                catch
                {
                    request.SendString("{}", "application/json");
                }
                return true;
            }));

            server.AddRouteFile("/", frontend + "index.html", new Dictionary<string, string>(), true, true, false, 0, false, 0);
            server.AddRouteFile("/style.css", frontend + "style.css", new Dictionary<string, string>(), true, true, false, 0, false, 0);
            server.AddRouteFile("/script.js", frontend + "script.js", new Dictionary<string, string>(), true, true, false, 0, false, 0);
            server.AddRouteFile("/admin", frontend + "admin.html", new Dictionary<string, string>(), true, true, false, accessCheck);
            server.AddRouteFile("/console", frontend + "console.html", new Dictionary<string, string>(), true, true, false, accessCheck);
            server.StartServer(config.port);
        }
    }
}
