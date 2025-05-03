# MP-AudioPlayer

A music player built around the functionality of a StreamIt Lisa Compact audio player.  
This player is meant to be a drop-in replacement that allows multicasting music over a network connection, but also allows for physical audio output from the Raspberri Pi




# Getting Started






### Download Raspbian Lite image/Flash SD Card

There is a tool [provided by the Raspberry Pi foundation that makes this really easy.](https://www.raspberrypi.com/software/)  You can install this tool on linux using

> sudo apt install rpi-imager

If you'd like to download the image and flash using a different utility like [rufus](https://rufus.ie/en/) the image source is provided below.  It is strongly recommended to use the official application because it can create user accounts and even set up network connections before the sd card is installed in the Pi.

Raspbian Lite image can be found from the [Official Raspberry Pi source](https://www.raspberrypi.com/software/operating-systems/).  Make sure to choose an image that is compatible with the version of Pi you are using.  Raspberry Pi 3+B is currently supported using this software. 

The currently used image is Raspberry Pi OS (Legacy) Lite 64-bit, with a release date of March 12th, 2024.  Other versions have not been tested. 

> [!TIP]
> NOTE:  If you use a 3rd party imager the default keyboard layout will be set to a UK standard, swapping the pound for British and a few other things.  You can change this with  
> >sudo raspi-config 

  
### System Configuration

Various system components can be adjusted using 

> sudo raspi-config

You will need to change the following: 

 - [ ] System Options > Audio > Change to "0 bcm2835 Headphones"
 - [ ] Interface Options > SSH > Change to "Enabled"

> [!TIP]  
> If you used a custom imager, you will need to set the keyboard layout: Localization Options > Keyboard
> then select your keyboard layout

The default network manager is a program called dhcpcd.  It's config file can be edited with 
> sudo nano /etc/dhcpcd.conf

We will edit this file so the Pi has a static IP address to stream from

Comment out all lines except for 
```
# Use the hardware address of the interface for the Client ID.
clientid

# Persist interface configuration when dhcpcd exits.
persistent

# no ipv6
noipv6rs
noipv6

# Example static IP configuration:
interface eth0
static ip_address=your ip here/cidr
static routers=your gateway here
static domain_name_servers=dns servers separated by space here
```

Disable unused services using the following commands: 
```
sudo systemctl disable bluetooth.service
sudo systemctl disable avahi-daemon.service
```
Disable system swap to limit sd card writes
```
sudo dphys-swapfile swapoff
sudo dphys-swapfile uninstall
```

>[!TIP]
> Reboot after at this point and confirm that everything is still working. 

### Install ffmpeg software

[ffmpeg](https://ffmpeg.org) is a complege, cross-platform solution to record, convert, and stream audio and video.  It is the application we will use to multicast our audio and receive it on the other end.  Install it with 
> sudo apt install ffmpeg

Confirm the installation was successful: 
> ffmpeg 

You should see a header similar to the following: 

![Screenshot of ffmpeg output](/assets/ffmpeg_output_sm.png)

### Install Dotnet Core SDK

This application has been tested using dotnet core version 7.0.200. Details about installing dotnet sdk's can be found [here](https://learn.microsoft.com/en-us/dotnet/iot/deployment) 

Run the following command to install dotnet core 7's SDK

> curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 7.0.200

Confirm successful installation by running 

> dotnet --info

### Set Up Streaming applications

- [ ] Create app directories 
    - in /opt/, create all necessary directories by running the command
> sudo mkdir -p /opt/player /mnt/tmpfs
    
- [ ] Set app dir permissions (to make the rest easier)
> sudo chown [user]:[user] /opt/player

- [ ] Create tmpfs as a working directory (so we can avoid reads and writes on the sd card)
    - using nano, edit /etc/fstab to add the following
```
#tmpfs for media
tmpfs /mnt/tmpfs tmpfs defaults,noatime,nosuid,size=250M 0 0
``` 

- [ ] Upload application files to Pi
    - Download and compile source from the repo
```
cd ~/M-AudioPlayer/src/
dotnet publish
```
Copy the files to the Pi
> scp ~/MP-AudioPlayer/src/bin/Debug/net7.0/publish/* user@[your pi's address]:/opt/player/

- [ ] Create base config file
```
cat << EOF > /opt/player/config
mode=2
IspName=""
IspDbUrl=
UdSwEnable=1
LiveComUrl=
LiveComEnable=1
IspUdUrl=
RcUrl=
RcInterval=15
SchedEnable=1
SchedUrl=
SafUrl=
language=EN
TnetEnable=0
ch1name=""
ch1url=""
ch2name=
ch2url=

EOF
```
- [ ] Establish player serial number (this is given by streaming provider)
```
cat << EOF > /opt/player/serial
[serial]
EOF
```
- [ ] Create systemd unit files
    - run the following commands
```
cat << EOF > /opt/player/mp-music-player.service
[Unit]
Description=mp-music-player configured to work with streaming provider
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=$(which dotnet) /opt/player/mp-music-player.dll --config /opt/player/liveConfig --serial $(cat /opt/player/serial) --working-dir /mnt/tmpfs  
Restart=on-failure
RestartSec=15s


[Install]
WantedBy=multi-user.target
EOF
```

```
cat << EOF > /opt/player/mp-music-receiver.service
[Unit]
Description=Auto play ffplay at boot
After=network-online.target
Wants=network-online.target

[Service]
ExecStart=$(which ffplay) -nodisp -hide_banner udp://224.0.0.7:5004
Restart=always
RestartSec=10s

[Install]
WantedBy=multi-user.target
EOF
```
Copy service files to systemd location
> sudo cp /opt/player/*.service /lib/systemd/system/

Reload systemd
> sudo systemctl daemon-reload

Enable and start the services
```
sudo systemctl enable mp-music-receiver.service
sudo systemctl enable mp-music-player.service
sudo systemctl start mp-music-receiver.service
sudo systemctl start mp-music-player.service
```
Monitor the status of each service with 
> sudo systemctl status {service}


You should now have a working player connected to the streaming service.  On the same network, connect a device with ffmpeg libraries installed and run the following command to receive audio streamed over the local network:
> ffplay -nodisp udp://224.0.0.7:5004

>[!TIP]
>You can use any software that can receive and play audio from a UDP source.  Run the player dotnet service manually with no arguments to
>see usage options

At this point you should be able to manage the player through the streamer's portal. 

Note: if you restart the player throught the portal there is a bug that does not allow the player to reconnect immediately

Application researched and developed by Caleb Williams, Movie Palace Inc. 
