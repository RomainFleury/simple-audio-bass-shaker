# Simple Bass Shaker Router

Hi, I just wanted a simple solution to get some bass action anywhere.

This is a small Windows app that listens to one audio output, keeps the low end, and sends that to another output — for a bass shaker, without Voicemeeter.

## What it does

1. Pick the **source** (where your game / music / movie is playing).
2. Pick the **shaker** (the device wired to your amp).
3. Set a **cutoff** (default 80 Hz) and a **level**.
4. Hit **Start**.
5. Optionally turn on **Live view** for the signal meter and a 5-second pass vs reject energy chart. Leave it off (or minimize the window) while gaming so the app stays light.

The source keeps playing the full mix. The shaker gets a filtered mono copy of the bass.

## Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Run it

```powershell
dotnet run --project SimpleBassShakerRouter.csproj
```

Or build a release:

```powershell
dotnet build -c Release
```

Then run `bin\Release\net8.0-windows\SimpleBassShakerRouter.exe`.

## Notes

- Use two different devices. Same device would feed the shaker back into itself.
- Stereo mixes both channels. Surround uses front left, front right, and the LFE channel.
- A fixed high-pass at 18 Hz blocks useless infrasonic rumble.
- Apps in exclusive mode may not be captured.
