# V380 Decoder

Extract video and audio from encrypted V380 camera. Newer V380 cameras use encryption for video and audio streams. This project is the result of reverse engineering the V380 Pro APK to discover the decryption methods.

This is a port of [prsyahmi/v380](https://github.com/prsyahmi/v380) with significant enhancements:
- ✅ Audio and video decryption
- ✅ RTSP server output
- ✅ ONVIF support
- ✅ Web UI and REST API for camera control
- ✅ Snapshot API
- ✅ Cloud relay streaming support

## Tested Cameras

**Note:** I've only tested with 2 V380 cameras running device version 31 with H264 stream.

**Camera 1:**
- Software: `AppEV2W_VA3_V2.5.9.5_20231211`
- Firmware: `Hw_AWT3710D_XHR_V1.0_WF_20230519`

**Camera 2:**
- Software: `AppEV2W_VA3_V1.3.7.0_20231211`
- Firmware: `Hw_AWT3610E_XHR_E_V1.0_WF_20230607`

## Requirements

- .NET 10 SDK (for building from source)
- FFmpeg (optional, for snapshot or piping video/audio output)

## Command Line Arguments

| Argument | Default | Required | Description |
|----------|---------|----------|-------------|
| `--id` | - | ✅ Yes | Camera device ID |
| `--username` | `admin` | ✅ Yes | Camera username |
| `--password` | - | ✅ Yes | Camera password |
| `--ip` | - | ⚠️ If LAN | Camera IP address (required for LAN source) |
| `--port` | `8800` | No | Camera port |
| `--source` | `lan` | No | Connection source: `lan` or `cloud` |
| `--output` | `rtsp` | No | Output mode: `video`, `audio`, or `rtsp` |
| `--enable-onvif` | `false` | No | Enable ONVIF server (requires `output=rtsp`) |
| `--enable-api` | `false` | No | Enable Web UI and REST API |
| `--enable-mjpeg` | `false` | No | Enable Mjpeg stream |
| `--rtsp-port` | `8554` | No | RTSP server port |
| `--http-port` | `8080` | No | Web server port (for ONVIF/API) |
| `--secure` | `false` | No | Enable authentication for ONVIF, RTSP and API. Uses the same username and password as the V380 camera |
| `--debug` | `false` | No | Enable debug logging |
| `--discover` | `false` | No | Discover camera |
| `--help` | `false` | No | Print help |

## Usage Examples

Download Latest [Release](https://github.com/PyanSofyan/V380decoder/releases/latest)

### Find Camera Device
```bash
./V380Decoder --discover
```
### Video Output (pipe to FFplay)
```bash
./V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --output video | ffplay -f h264 -i pipe:0
```

### Audio Output (pipe to FFplay)
```bash
./V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --output audio | ffplay -f alaw -ar 8000 -ac 1 -i pipe:0
```

### RTSP Server
```bash
# Default (RTSP mode)
./V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2

# Or explicitly specify
./V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --output rtsp
```

**Access stream:**
```
rtsp://192.168.1.3:8554/live
```

**Access snapshot:**
```
http://192.168.1.3:8080/snapshot
```

### Cloud Streaming
Streaming via relay server (relay IP automatically detected):
```bash
./V380Decoder --id 12345678 --username admin --password password --source cloud
```

## ONVIF Support

Tested with [Onvif Device Manager](https://sourceforge.net/projects/onvifdm/) (ODM), [Onvif Integration](https://www.home-assistant.io/integrations/onvif/) Home Assistant and [Shinobi](https://shinobi.video/)

**Enable ONVIF:**
```bash
./V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --enable-onvif
```

**Access ONVIF endpoint:**
```
http://192.168.1.3:8080/onvif/device_service
```

**Features:**
- ✅ Live video
- ✅ PTZ control (pan/tilt)
- ✅ Imaging settings (light control)
- ✅ Media profiles
- ✅ Device discovery

## Web UI & REST API

Control your camera through a web interface or REST API.

**Enable MJPEG Stream:**
```bash
./V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --enable-mjpeg
```

### MJPEG Endpoint
```
http://192.168.1.3:8080/mjpeg
```

**Enable API:**
```bash
./V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --enable-api
```

### Web UI
```
http://192.168.1.3:8080
```

### REST API Endpoints

**PTZ Control:**
```bash
curl -X POST http://192.168.1.3:8080/api/ptz/right
curl -X POST http://192.168.1.3:8080/api/ptz/left
curl -X POST http://192.168.1.3:8080/api/ptz/up
curl -X POST http://192.168.1.3:8080/api/ptz/down
```

**Light Control:**
```bash
curl -X POST http://192.168.1.3:8080/api/light/on
curl -X POST http://192.168.1.3:8080/api/light/off
curl -X POST http://192.168.1.3:8080/api/light/auto
```

**Image Mode:**
```bash
curl -X POST http://192.168.1.3:8080/api/image/color   # Day mode (color)
curl -X POST http://192.168.1.3:8080/api/image/bw      # Night mode (B&W with IR)
curl -X POST http://192.168.1.3:8080/api/image/auto    # Auto switch
curl -X POST http://192.168.1.3:8080/api/image/flip    # Flip image 180°
```

**Status:**
```bash
curl http://192.168.1.3:8080/api/status
```

## Build from Source

1. **Install .NET 10 SDK**
   - Download from: https://dotnet.microsoft.com/download/dotnet/10.0

2. **Clone repository**
   ```bash
   git clone https://github.com/PyanSofyan/V380Decoder.git
   cd V380Decoder
   ```

3. **Build**
   ```bash
   dotnet build
   ```
4. **Run with arguments**
   ```bash
   dotnet run -- --id 12345678 --username admin --password password --ip 192.168.1.2
   ```

5. **Publish (optional - for deployment)**
   ```bash
   # Linux x64
   dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true -o ./publish/linux-x64
   
   # Linux ARM64 (Raspberry Pi 3+)
   dotnet publish -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true -o ./publish/linux-arm64

   # Linux ARM (Raspberry Pi, etc)
   dotnet publish -c Release -r linux-arm --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true -o ./publish/linux-arm

   # Windows x64
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=true -o ./publish/win-x64

   # Windows x86 (Built without trimming due to IL Trimmer compatibility)
   dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ./publish/win-x86
   
   ```

## Run as System Service

### Linux (systemd)

Create `/etc/systemd/system/v380decoder.service`:
```ini
[Unit]
Description=V380 Camera Decoder
After=network.target

[Service]
Type=simple
User=YOUR_USERNAME
WorkingDirectory=/home/YOUR_USERNAME/v380decoder
ExecStart=/home/YOUR_USERNAME/v380decoder/V380Decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --enable-onvif --enable-api
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable v380decoder
sudo systemctl start v380decoder
sudo systemctl status v380decoder
```

### Docker

Build and run:
```bash
docker build -t v380decoder .
docker run -d --restart unless-stopped --network host v380decoder --id 12345678 --username admin --password password --ip 192.168.1.2 --enable-onvif --enable-api
```

## Software-calibrated PTZ presets

The camera does not provide verified absolute PTZ coordinates. Calibration drives the camera to its left and down physical stops, establishing a repeatable reference. Because the V380 direction command is a short step rather than a continuous motor-start command, the decoder repeats it every 250 ms. `pan` and `tilt` record these command windows from the reference corner, not degrees or camera-reported coordinates.

The state file defaults to `/data/ptz-state.json`. Mount `/data` to persistent host storage when using Docker:

```bash
docker run -d --restart unless-stopped --network host \
  -v /opt/v380decoder-data:/data \
  v380decoder --id 12345678 --username admin --password "$V380_PASSWORD" \
  --ip 192.168.1.2 --enable-onvif --enable-api
```

### API

All timed movement requests require a duration from 250 to 10000 milliseconds in 250 ms increments. The decoder repeats the direction command every 250 ms for that window and then sends the camera stop command. A failed, cancelled, or manually overridden move is not added to the logical position.

```bash
# Establish the left/down reference using measured full-travel times. Calibration
# clears presets saved using an older coordinate system.
curl -X POST "http://localhost:8080/api/ptz/calibrate?panTravelMs=${PAN_TRAVEL_MS}&tiltTravelMs=${TILT_TRAVEL_MS}"

# Move right for 500 ms and update software position.
curl -X POST http://localhost:8080/api/ptz/move \
  -H 'Content-Type: application/json' \
  -d '{"direction":"right","durationMs":500}'

# Inspect the software-calibrated state.
curl http://localhost:8080/api/ptz/calibration/status

# Save, list, visit, and delete named presets.
curl -X POST http://localhost:8080/api/ptz/presets \
  -H 'Content-Type: application/json' -d '{"name":"front_door"}'
curl http://localhost:8080/api/ptz/presets
curl -X POST http://localhost:8080/api/ptz/presets/front_door/goto
curl -X DELETE http://localhost:8080/api/ptz/presets/front_door

# Save the current logical position temporarily, then restore it later.
curl -X POST http://localhost:8080/api/ptz/save-position
curl -X POST http://localhost:8080/api/ptz/restore-position
```

Preset and temporary-position recall always re-homes against the physical stops before moving to the saved position. This corrects drift caused by movement from the native V380 app or any other unobserved controller. The original `/api/ptz/up`, `/down`, `/left`, and `/right` endpoints remain available for backwards compatibility, but they do not update software position. `POST /api/ptz/stop` also cancels an active timed move.

## Acknowledgements

- [prsyahmi/v380](https://github.com/prsyahmi/v380) - Original V380 reverse engineering work
- [Cyberlink Security](https://cyberlinksecurity.ie/vulnerabilities-to-exploit-a-chinese-ip-camera/) - V380 vulnerability research
- Tools used: [Wireshark](https://www.wireshark.org/), [PacketSender](https://packetsender.com/), [JADX](https://github.com/skylot/jadx), [Ghidra](https://github.com/NationalSecurityAgency/ghidra), [Frida](https://github.com/frida/frida)
