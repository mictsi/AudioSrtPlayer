# Audio + SRT Player

A .NET 10 WPF desktop app that plays an audio file and synchronizes subtitle highlighting from an SRT file.

## Prerequisites

- Windows (WPF app)
- .NET SDK 10.x

Check SDK:

```powershell
dotnet --version
```

## Build

From the repository root:

```powershell
dotnet build .\AudioSrtPlayer\AudioSrtPlayer.csproj
```

Release build:

```powershell
dotnet build .\AudioSrtPlayer\AudioSrtPlayer.csproj -c Release
```

## Run

From the repository root:

```powershell
dotnet run --project .\AudioSrtPlayer\AudioSrtPlayer.csproj
```

Or from the project folder:

```powershell
cd .\AudioSrtPlayer
dotnet run
```

## Clean

Clean default configuration:

```powershell
dotnet clean .\AudioSrtPlayer\AudioSrtPlayer.csproj
```

Clean Release outputs:

```powershell
dotnet clean .\AudioSrtPlayer\AudioSrtPlayer.csproj -c Release
```

## Publish (optional)

Framework-dependent publish:

```powershell
dotnet publish .\AudioSrtPlayer\AudioSrtPlayer.csproj -c Release -o .\publish\win
```

## Usage

1. Start the app.
2. Click **Open Audio** and select an audio file.
3. Click **Open SRT** and select an `.srt` subtitle file.
4. Use **Play / Pause / Stop** and the seek slider.
5. Use subtitle search to jump between matching lines.

Keyboard shortcuts:

- `Space`: Play/Pause
- `Left Arrow`: Seek backward
- `Right Arrow`: Seek forward

## Troubleshooting

- If build fails with a file lock error on `AudioSrtPlayer.exe`, close the running app and build again.
- If subtitles do not appear, verify the selected file is a valid SRT file with timestamp lines.
