using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace mp_music_player;

public class StreamitPlayer
{
    private Config _config = new();
    private string _configPath; 

    // timer stuff
    private Timer _heartBeatTimer = null;
    private DateTime _lastHeartBeat;  
    private string _userAgent;

    // define flags for ffmpeg general and input
    private string _ffmpegStartFlags;
    // ffmpeg output flags (url and format options)
    private string _ffmpegOutputFlags;
    private string _playerState; // stopped, playing, error    
    private string _workingDir;
    // constructor
    public StreamitPlayer(string[] args)
    {
        Logger.LogMessage("Initializing Player...", Log.Info);
        _config = Init(args);
        _userAgent = "NSPlayer/4.0 Lisa Compact - Plus/01.54.00 s/n:" + $"{_config.PlayerSerial}";
    }

    public async Task Start()
    {


        // update config if needed
        LiveConfig newLc = new();
        newLc = await GetLiveConfig();
        if (newLc != null)
        {
            bool confChange = false;
            if(newLc.IspDbUrl != _config.liveConfig.IspDbUrl)
            {
                Logger.LogMessage($"Received new IspDbUrl: {newLc.IspDbUrl}", Log.Info);
                _config.liveConfig.IspDbUrl = newLc.IspDbUrl; 
                confChange = true;
            } 
            if(newLc.LiveComUrl != _config.liveConfig.LiveComUrl)
            {
                Logger.LogMessage($"Received new LiveComeUrl: {newLc.LiveComUrl}", Log.Info);
                _config.liveConfig.LiveComUrl = newLc.LiveComUrl; 
                confChange = true;
            }
            if(newLc.LiveComEnable != _config.liveConfig.LiveComEnable)
            { 
                Logger.LogMessage($"Received new LiveComEnable: {newLc.LiveComEnable}", Log.Info);
                _config.liveConfig.LiveComEnable = newLc.LiveComEnable;
                 confChange = true;
            }
            if(newLc.RcUrl != _config.liveConfig.RcUrl)
            {
                Logger.LogMessage($"Received new RcUrl: {newLc.RcUrl}", Log.Info);
                 _config.liveConfig.RcUrl = newLc.RcUrl; 
                 confChange = true;
            }
            if(newLc.RcInterval != _config.liveConfig.RcInterval) 
            {
                Logger.LogMessage($"Received new RcInterval: {newLc.RcInterval}", Log.Info);
                _config.liveConfig.RcInterval = newLc.RcInterval; 
                confChange = true;
            }
            if(newLc.ch1name != _config.liveConfig.ch1name)
            { 
                Logger.LogMessage($"Received new ch1name: {newLc.ch1name}", Log.Info);
                _config.liveConfig.ch1name = newLc.ch1name; 
                confChange = true;
            }
            if(newLc.ch1url != _config.liveConfig.ch1url) 
            {
                Logger.LogMessage($"Received new ch1url: {newLc.ch1url}", Log.Info);
                _config.liveConfig.ch1url = newLc.ch1url; 
                confChange = true;
            }

            // take care of config file if there are changes
            if(confChange == true)
            {   
                Logger.LogMessage("################# NEW CONFIG #################", Log.Warning);
                StringBuilder cString = new();
                
                foreach (PropertyInfo propertyInfo in newLc.GetType().GetProperties())
                {
                    Logger.LogMessage(propertyInfo.Name + "=" + propertyInfo.GetValue(propertyInfo, null) + System.Environment.NewLine, Log.Info);
                    cString.AppendLine(propertyInfo.Name + "=" + propertyInfo.GetValue(propertyInfo, null) + System.Environment.NewLine);
                }

                // backup exising config
                if(File.Exists(Path.GetFullPath(_configPath))) File.Copy(Path.GetFullPath(_configPath), Path.GetFullPath(_configPath) + "_bak");
                // write chang to config file
                using (StreamWriter outputFile = new StreamWriter(Path.Combine(Path.GetFullPath(_configPath), "config")))
                {
                    outputFile.Write(cString);
                }    
            }
        }
        else
        {
            Logger.LogMessage("Received null response from config server. Using default config", Log.Error);
        }

        
        
        // start song player task (hopefully in a new thread)
        var tS = new CancellationTokenSource();
        CancellationToken ct = tS.Token;
        string[] flags = {_ffmpegStartFlags, _ffmpegOutputFlags};
        SongPlayer songPlayer = new SongPlayer(_config, _workingDir, flags);

        LiveController controller = new LiveController(_config.liveConfig.LiveComUrl, _config.PlayerSerial);

        
        // setup heartbeat, Must be created after SongPlayer object is instantiated because of status var
        _heartBeatTimer = new Timer(CheckToSend_Heartbeat, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Logger.LogMessage("Starting player", Log.Info);

        Task player = Task.Run(() => songPlayer.Play());
        Task liveController = Task.Run(() => controller.Run());

        // monitor controller and player and restart each if there is a problem
        while(true)
        {
            if(player.IsCompleted || player.IsFaulted || player.IsCanceled)
            {
                // get rid of the old one
                player.Dispose();
                // we have to create a "new" task instead of just calling player.Start(); on the old one
                player = Task.Run(() => songPlayer.Play());
            }
            if(liveController.IsFaulted || liveController.IsCompleted || liveController.IsCanceled)
            {
                liveController.Dispose();
                liveController = Task.Run(() => controller.Run());
            }

            Thread.Sleep(30);
        }
        
    }
    private Config Init(string[] args)
    {
        LiveConfig lc =  new LiveConfig();
        Config config = new Config();
        for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--serial")
                {
                    // see if it's a file and process that first, if not then assume it's a simple string
                    if(File.Exists(Path.GetFullPath(args[i+1])))
                    {
                        string line;
                        try
                        {
                            //Pass the file path and file name to the StreamReader constructor
                            StreamReader sr = new StreamReader(Path.GetFullPath(args[i+1]));
                            line = sr.ReadLine();
                                                            
                            if (line != null) 
                            {
                                config.PlayerSerial = line;
                            }
                            else
                            {
                                Logger.LogMessage("Error: No serial number found", Log.Error);
                                Usage();
                            }
                            //close the file
                            sr.Close();
                        }
                        catch(Exception e)
                        {
                            Logger.LogMessage("Error: Could not parse given serial number", Log.Error);
                            Console.WriteLine("Exception: " + e.Message);
                            Usage();
                        }
                    }
                    else if (args[i+1] != "") config.PlayerSerial = args[i+1]; 
                    else
                    {
                        Logger.LogMessage("Error: Could not parse Serial Number", Log.Error);
                        Usage();
                    }
                }
                // config requires a file (same format as streaming service returns from portal)
                else if (args[i] == "--config")
                {
                    if (File.Exists(Path.GetFullPath(args[i+1])))
                    {
                        _configPath = Path.GetFullPath(args[i+1]);
                        string line;
                        List<string> listConf = new List<string>();
                        string stringConf;
                        try
                        {
                            //Pass the file path and file name to the StreamReader constructor
                            StreamReader sr = new StreamReader(Path.GetFullPath(args[i+1]));
                        
                            line = sr.ReadLine();
                            while (line != null)
                            {
                                line = sr.ReadLine();
                                listConf.Add(line);
                            }
                            sr.Close();
                            stringConf = String.Join('\n', listConf);
                            lc = ParseConfig(stringConf);
                            config.liveConfig = lc;

                        }
                        catch(Exception e)
                        {
                            Logger.LogMessage("Error: Could not parse given config", Log.Error);
                            Console.WriteLine("Exception: " + e.Message);
                            System.Environment.Exit(1);
                        }
                    }
                    else
                    {
                        Logger.LogMessage("Could not find log file", Log.Error);
                    }
                }
                else if (args[i] == "--ffmpeg-input-flags") _ffmpegStartFlags = args[i+1];
                else if (args[i] == "--ffmpeg-output-flags") _ffmpegOutputFlags = args[i+1];
                else if (args[i] == "--working-dir") _workingDir = args[i+1];   
            }

            // set default ffmpeg flags
            if (_ffmpegStartFlags == null) _ffmpegStartFlags = " -re -hide_banner -loglevel error ";

            if (_ffmpegOutputFlags == null) _ffmpegOutputFlags = "-f mpegts udp://224.0.0.7:5004";

            if (_workingDir == null) Usage();
            if (_config == null) Usage();
            return config;
    }

    // get config from cc server
    private async Task<LiveConfig> GetLiveConfig()
    {
        using (HttpClient client = new())
        {
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _userAgent);
        try
        {
            string lc = await client.GetStringAsync(_config.liveConfig.IspDbUrl.ToString());   
            client.Dispose();
            LiveConfig newLiveConfig = new LiveConfig();
            newLiveConfig = ParseConfig(lc);

            return newLiveConfig;
        }
        catch (Exception e)
        {
            Logger.LogMessage("Error getting config from server: " + e, Log.Error);
            return null;
        }

        }
    }

    // get config from input string and parse (from file)
    private static LiveConfig ParseConfig(string data)
    {
        LiveConfig config= new();
        string[] vals = data.Split('\n');

        for(int i = 0; i < vals.Length; i++)
        {
            string[] line = vals[i].Split('='); 
            switch(line[0]){
                case "IspName": config.IspName = line[1]; break;
                case "IspDbUrl": config.IspDbUrl = line[1]; break;
                case "LiveComUrl": config.LiveComUrl = line[1]; break;
                case "LiveComeEnable": config.LiveComEnable = Int32.Parse(line[1]); break;
                case "RcUrl": config.RcUrl = line[1]; break;
                case "RcInterval": config.RcInterval = Int32.Parse(line[1]); break;  
                case "SchedEnable": config.SchedEnable = Int32.Parse(line[1]); break;
                case "SchedUrl": config.SchedUrl = line[1]; break;
                case "ch1name": config.ch1name = line[1]; break;
                case "ch1url": config.ch1url = line[1]; break;
            }
        }
        return config;
    }

    private void CheckToSend_Heartbeat(object state)
    {


        if (_lastHeartBeat <= DateTime.UtcNow.AddMinutes(-15))
        {   
            Task.Run(async () =>
            {
                try
                {
                    string userAgent = "NSPlayer/4.0 Lisa Compact - Plus/01.54.00 s/n:" + $"{_config.PlayerSerial}";
                    _playerState = SongPlayer.GetPlayerState();
                    string server = _config.liveConfig.RcUrl;
                    int port = 80;
                    StringBuilder sr = new();

                    sr.AppendLine("[TYPE] Lisa Compact - Plus");
                    sr.AppendLine($"[SNUM] {_config.PlayerSerial}" );
                    sr.AppendLine("[APPVER] 01.54.00");
                    sr.AppendLine("[CONF] STREAMIT");
                    sr.AppendLine("[PWR] ON");
                    sr.AppendLine("[SRC] Internet: Movie Palace STU");
                    sr.AppendLine($"[PSTAT] {_playerState}");
                    sr.AppendLine("[PUD] Idle");
                    sr.AppendLine("[CRD] Not present or not mounted");
                    sr.AppendLine("[MISC] timeout: 0, disconnect: 0, buf.emp: 0, fallback: 0");
                    //Console.WriteLine(sr.ToString());
                    await SendStringToSocketAsync(server, port, sr.ToString());

                   // message tel server
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error Sending Heartbeat: {ex.Message}", Log.Error);
                }
                });

                _lastHeartBeat = DateTime.UtcNow;
        }

    }
    static async Task SendStringToSocketAsync(string server, int port, string message)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                await client.ConnectAsync(server, port);
                using NetworkStream stream = client.GetStream();
                
                byte[] data = Encoding.ASCII.GetBytes(message);
                await stream.WriteAsync(data, 0, data.Length);
                //Console.WriteLine("Sent: {0}", message);

                // Optional: receive a response from the server
                byte[] buffer = new byte[256];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                //Console.WriteLine("Received: {0}", response);
                client.Close();
            };
        }
        catch (Exception e)
        {
            Logger.LogMessage($"Exception: {e.Message}", Log.Error);
        }
    }

    private void DeleteOrphanedFiles()

    {
        var files = Directory.GetFiles(System.AppDomain.CurrentDomain.BaseDirectory);
        foreach (var file in files)
        {
            if (file.EndsWith(".ogg"))
            {
                File.Delete(file);
            }
        }
    }  

    public static void Usage()
    {
        StringBuilder sr = new();
        sr.AppendLine("Usage: player [OPTIONS]\n");
        sr.AppendLine("     --config               Required: full path to config file");
        sr.AppendLine("     --serial               Required: player serial number ");
        sr.AppendLine("     --working-dir          Required: full path to working directory");
        sr.AppendLine("                            where media will be downloaded");
        sr.AppendLine();
        sr.AppendLine("     --ffmpeg-input-flags   default is \' -re -hide_banner -loglevel error\'");
        sr.AppendLine("     --ffmpeg-output-flags  default is \'-f mpegts udp://224.0.0.7:5004\'");
        sr.AppendLine();
        //sr.AppendLine("If you encounter bugs please send an email with issue details to caleb.williams@wyomovies.com");
        Console.Write(sr);
        System.Environment.Exit(1);
    }  
}

public class LiveConfig
{
    public string IspName { get; set; }
    public string IspDbUrl { get; set; }    // config url
    public string LiveComUrl { get; set; }  // live control url
    public int LiveComEnable { get; set; }
    public string RcUrl { get; set; }       // monitoring url
    public int RcInterval { get; set; }     // time in minutes to check in ^^
    public string SchedUrl { get; set; }    // schedul of tasks
    public int SchedEnable { get; set; }
    
    public string ch1name { get; set; }     // channel name (not very important)
    public string ch1url { get; set; }      // channel url (very important) 

}

public class Config
{
    public string PlayerSerial { get; set; }
    public LiveConfig liveConfig{ get; set; }
    
}