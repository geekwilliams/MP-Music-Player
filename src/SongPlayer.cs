using System.Diagnostics;
using System.Net.Http.Headers;

namespace mp_music_player;

public class SongPlayer
{
    private static string _songPlayerState; // Playing, Loading, Error
    private string _channel;

    private string _userAgent;
    private string _workingDir;
    private string[] _ffmpegFlags;
    private Config _config;

    public SongPlayer(Config config, string workingDir, string[] ffmpegFlags)
    {
        _config = config;
        _songPlayerState = "Loading";
        _channel = _config.liveConfig.ch1url;
        _userAgent = "NSPlayer/4.0 Lisa Compact - Plus/01.54.00 s/n:" + $"{_config.PlayerSerial}";
        _workingDir = workingDir ?? throw new Exception("No working directory specified");
        // flags have default value so no need to throw exception
        _ffmpegFlags = ffmpegFlags;
    }
    public static string GetPlayerState()
    {
        return _songPlayerState;
    }

    public async Task Play()
    {
        _songPlayerState = "OK";
        Logger.LogMessage("Playing...", Log.Info);
        while(true)
        {
            string song = await GetNextSong();
            if (song == null)
            {   
                // sleep so we don't ddos the server
                Thread.Sleep(30*1000);
            }
            else
            {
                try
                {
                    _songPlayerState = "Playing stream";
                    PlaySong(song);
                    DeleteMusicFiles();
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error occured playing song: {ex.Message}", Log.Error);
                    Logger.LogMessage("Waiting for 60 seconds", Log.Warning);
                    Thread.Sleep(60*1000);
                }
            }
        }

    }
    private async Task<string> GetNextSong()
    {
        try
        {
            using(HttpClient client = new())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _userAgent);
                string playlist = await client.GetStringAsync(_config.liveConfig.ch1url.Replace("\"",""));

                // download song to local storage
                if(playlist != null)
                {
                    var song = await client.GetAsync(playlist);
                    string songGuid = Guid.NewGuid().ToString();    
                    using(var fs = new FileStream(_workingDir + "/" + songGuid + ".ogg", FileMode.CreateNew))
                    {
                        //Logger.LogMessage($"Downloading song {song}", Log.Info);
                        await song.Content.CopyToAsync(fs);
                    };
                    return _workingDir + "/"+ songGuid + ".ogg";
                }
                else
                {
                    return null;
                }


            }
        }
        catch(Exception ex)
        {
            Logger.LogMessage("Something went wrong with new song operation: " + ex, Log.Error);
            return null;
        }

    }


    private void PlaySong(string songPath)
    {
        try
        {
            using(Process player = new Process())
            {
                //Logger.LogMessage($"Playing {songPath}...", Log.Info);
                player.StartInfo.FileName = "/usr/bin/ffmpeg";
                player.StartInfo.Arguments = $" {_ffmpegFlags[0]} -i {songPath} {_ffmpegFlags[1]}";
                player.Start();
                player.WaitForExit();
            };
        }
        catch (Exception ex)    
        {
            Logger.LogMessage($"Error: Couldn't play the song: {ex.Message}", Log.Error);
        }
    }

    private void DeleteMusicFiles()
    {
        try
        {
            var files = Directory.GetFiles(_workingDir);
            foreach (var file in files)
            {
                if(file.EndsWith(".ogg"))
                {
                    File.Delete(file);
                }
            }
        }
        catch(Exception ex)
        {
            Logger.LogMessage($"Error when deleting old *.ogg files: {ex.Message}", Log.Error);
        }
    }
}