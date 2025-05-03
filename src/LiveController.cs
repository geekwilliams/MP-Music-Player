using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace mp_music_player;
public class LiveController
{   
    private string _liveComUrl;
    private static string _playerSerial;
    private static DateTime _lastHeartbeat;
    public LiveController(string liveComUrl, string serial)
    {   
        _liveComUrl = liveComUrl;
        _playerSerial = serial;
    }

    public async Task Run()
    {

        // PAY ATTENTION TO SPACES AND ORDER OF \r\n ... stupid custom channels
        const string status = "\r\nstatus \n\r\n\r\nstatus = Playing stream\r\n (OGG), 95kbps\r\n>";
        const string curchan = "\r\ncurchan \n\r\ncurchan = 1\r\n>";
        // string volctrl2 = "\r\nvolctrl2 \n\r\nvolctrl2 = 86\r\n>";
        string poweroff = "\r\npw 0\r\n\r\n\r\nOK\r\n>";
        string poweron = "\r\npw 1\r\n\r\n\r\nOK\r\n>";

        IPAddress[] iPs = Dns.GetHostAddresses(_liveComUrl);
        IPAddress hostAddress = iPs[0];
        IPEndPoint endPoint = new(hostAddress, 80);
        using Socket client = new(
            hostAddress.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp
        );

        while (true)
        {
            if(!client.Connected)
            {
                try
                {
                    // if you want to know how often the player needs to reconnect  remove the comment, 
                    // otherwise, we're trying to limit writes to the sd card
                    // Logger.LogMessage("Reconnecting to control socket", Log.Warning);
                    await client.ConnectAsync(endPoint);
                    await client.SendAsync(Encoding.ASCII.GetBytes($"[{_playerSerial}]"), SocketFlags.None);
                    _lastHeartbeat = DateTime.UtcNow;
                }
                catch(Exception ex) 
                {
                    // errors are "broken pipe; software caused connection abort" 
                    // We'll ignore these and just let the loop try to sort itself
                }
            }
            else
            {}

            // if we start over the loop and it's been more than a minute then send the "heartbeat" again
            if(_lastHeartbeat <= DateTime.UtcNow.AddMinutes(-1)) 
            {
                
                if (client.Connected) 
                {
                    byte[] message = Encoding.ASCII.GetBytes($"[{_playerSerial}]");
                    try
                    {
                        await client.SendAsync(message, SocketFlags.None); 
                        _lastHeartbeat = DateTime.UtcNow; 
                    }
                    catch (Exception ex)
                    {
                        // if this errors out we're gonna do nothing and wait for the main connection to refresh
                    } 

                }          
            } 

            
            // main receive
            try
            {
                byte[] buffer = new byte[1024];
                int received = await client.ReceiveAsync(buffer, SocketFlags.None);
                string response = Encoding.ASCII.GetString(buffer, 0, received);
                if (response.Contains("status"))
                {
                    _ = await client.SendAsync(Encoding.ASCII.GetBytes(status), SocketFlags.None);
                }
                else if (response.Contains("curchan"))
                {
                    // for our purposes the channel will always be 1, so that's what's being sent back 
                    _ = await client.SendAsync(Encoding.ASCII.GetBytes(curchan),SocketFlags.None);
                }
                else if (response.Contains("volctrl2"))
                {
                    // set vol with command amixer sset PCM {VOL}%  
                    // System saves volume automatically, so no need to use amixer store
                    // get rid of newline at the end fucking with .Length count
                    string[] substring = response.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    if (substring[0].Length == 8)
                    {
                        //  Socket only requested volume
                        int curVol = GetSysVolume();
                        /*
                            Convert sys volume to volume server is expecting (86-124)
                        */
                        double correctedVolume = (double)curVol/100*38 + 86;
                        int cv = (int)Math.Round(correctedVolume);
                        _ = await client.SendAsync(Encoding.ASCII.GetBytes("\r\nvolctrl2 \n\r\nvolctrl2 = " + cv + "\r\n>"));
                    }
                    else
                    {

                        try 
                        {
                            int volumeLevel = Int32.Parse(substring[0].Substring(8));
                            /* 
                            Socket requests volume leves from 86-124.  We need to translate these to percentages to make it 
                            easy for the pi to figure out what to set as.  Also we need to set the max volume on the pie to 85% (Louder volumes get crunchy)
                            43 is the range, 81 being the lower bound.  If we subtract 81 from the requested vol we get a range in 43, which we can then get 
                            a decimal percentage.  Multiply by 85 and we get the real percentage we want
                            */

                            double volumeLAdjusted = (double)(volumeLevel - 86)/38*85;
                            Logger.LogMessage($"Server requested volume {volumeLevel}db.  Setting Pi volume to {(int)Math.Round(volumeLAdjusted)}%", Log.Info);
                            int setVol = SetSysVolume((int)Math.Round(volumeLAdjusted));
                            _ = await client.SendAsync(Encoding.ASCII.GetBytes($"\r\nvolctrl2 \n\r\nvolctrl2 = {setVol}\r\n>"));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage($"Unable to parse or set volume level requested: {ex}", Log.Error);
                        }
                    }
                    
                    
                }
                else if (response.Contains("pw 0"))
                {
                    // just echo back what the server sent for now
                    _ = await client.SendAsync(Encoding.ASCII.GetBytes(poweroff));
                }
                else if (response.Contains("pw 1"))
                {
                    // just echo back what the server sent for now
                    _ = await client.SendAsync(Encoding.ASCII.GetBytes(poweron));
                }
                else if (response.Contains("restart"))
                {
                    // server does not expect a response if this is called
                    using(Process p = new())
                    {
                        p.StartInfo.FileName = "/usr/sbin/shutdown";
                        p.StartInfo.Arguments = " -r now 'Player is restarting *NOW*'";
                        p.Start();
                        // we should be rebooting now so no more code execution
                    }
                }
            }
            catch(Exception ex)
            {
                if (ex.Message.Contains("Transport endpoint is not connected"))
                {
                    // blah blah
                    continue;
                }
                else
                {
                    Logger.LogMessage($"Error: {ex.Message}", Log.Error);
                }
                
            }
            Thread.Sleep(10);
            
        }
    }

    // Volume is saved and restored on boot by the system, no need to do it here. We can control current volume with the following code
    private int GetSysVolume()
    {
        using(Process proc = new())
        {
            proc.StartInfo.FileName = "/usr/bin/amixer";
            proc.StartInfo.Arguments = " -M get PCM";
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.UseShellExecute = false;

            StringBuilder response = new();
            proc.OutputDataReceived += (object sender, DataReceivedEventArgs e) => { if (!String.IsNullOrEmpty(e.Data)) response.Append(e.Data + "\n"); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.WaitForExit();
            string[] data = response.ToString().Split(System.Environment.NewLine); 
            string[] line = data[4].Split(" ");
            string output = GetStringBetweenCharacters(line[5], '[', '%');
            int vol = Int32.Parse(output);
            //Logger.LogMessage($"Volume: {vol}%", Log.Info);
            return vol;
        } 
    }
    
    private int SetSysVolume(int volume)
    {
        using(Process proc = new())
        {
            proc.StartInfo.FileName = "/usr/bin/amixer";
            proc.StartInfo.Arguments = $" -M set PCM {volume}%";
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.UseShellExecute = false;

            StringBuilder response = new();
            proc.OutputDataReceived += (object sender, DataReceivedEventArgs e) => { if (!String.IsNullOrEmpty(e.Data)) response.Append(e.Data + "\n"); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.WaitForExit();
            string[] data = response.ToString().Split(System.Environment.NewLine); 
            string[] line = data[4].Split(" ");
            string output = GetStringBetweenCharacters(line[5], '[', '%');
            int vol = Int32.Parse(output);
            Logger.LogMessage($"Volume set to {vol}%", Log.Info);
            return vol;
        }
    }

    public static string GetStringBetweenCharacters(string input, char charFrom, char charTo)
    {
        int posFrom = input.IndexOf(charFrom);
        if (posFrom != -1) //if found char
        {
            int posTo = input.IndexOf(charTo, posFrom + 1);
            if (posTo != -1) //if found char
            {
                return input.Substring(posFrom + 1, posTo - posFrom - 1);
            }
        }

        return string.Empty;
    }
}