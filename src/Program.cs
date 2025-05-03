using System.Diagnostics;
using System.IO;
using System.Net.Http.Headers;
using System.Net.Http;

using System.Net.Sockets;
using System.Text;
namespace mp_music_player;
public static class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 1) 
        {
            StreamitPlayer.Usage(); // prints args and exits program
        }
        StreamitPlayer streamitPlayer = new(args);
        await streamitPlayer.Start();      
    }
}