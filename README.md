<p align="center">
  <img src="src/Pookie.App/Assets/Branding/app-icon-128.png" width="96" height="96" alt="Pookie app icon">
</p>

<h1 align="center">Pookie</h1>

<p align="center">An independent SoundCloud desktop client for Windows, Linux and macOS</p>

<p align="center">
  <a href="https://github.com/freakdaniel/Pookie/releases">Download Pookie</a> ·
  <a href="#build-from-source">Build from source</a> ·
  <a href="https://github.com/freakdaniel/Pookie/issues">Report an issue</a>
</p>

## Inside the player

| | |
| --- | --- |
| **Native App** | Pookie runs as a native desktop app on Windows, Linux and macOS. Pause playback or skip a track from your system’s media controls while you work in another app |
| **Discovery** | Browse your feed and personal mixes. Search for tracks, people and playlists, or look up a lyric with [lrc.red](https://lrc.red) |
| **Playback** | Seek along the waveform, read timed comments and manage the queue. Open synced lyrics when a track has them |
| **Library** | Revisit your likes and listening history, edit playlists and change their covers |
| **Chats** | Share tracks and playlists in your SoundCloud conversations. With other Pookie users, you can send photos, videos and voice messages, reply to a specific message or react with an emoji. Search the conversation to find a track someone sent you |

## Download and run

Choose a package from the [Releases page](https://github.com/freakdaniel/Pookie/releases). Each download includes the .NET runtime. The commands below use version `0.0.1` as an example.

| Platform | Download | Install or run |
| --- | --- | --- |
| Debian / Ubuntu x64 | `.deb` | `sudo apt install ./Pookie-x.x.x-linux-x64.deb` |
| Fedora x64 | `.rpm` | `sudo dnf install ./Pookie-0.0.1-linux-x64.rpm` |
| Linux x64, standalone | `.AppImage` | `chmod +x Pookie-0.0.1-linux-x64.AppImage`, then `./Pookie-0.0.1-linux-x64.AppImage` |
| Linux x64, Flatpak | `.flatpak` | `flatpak install --user ./Pookie-0.0.1-linux-x64.flatpak`, then `flatpak run io.github.freakdaniel.Pookie` |
| Windows x64, installer | `win-x64.msi` | Open the installer, then launch Pookie from the Start menu |
| Windows x64, portable | `win-x64-portable.zip` | Extract the whole ZIP to a writable folder and open `win-x64/Pookie.exe` |
| macOS on Apple silicon | `osx-arm64.dmg` | Open the disk image and drag Pookie into Applications |
| macOS on Intel | `osx-x64.dmg` | Open the disk image and drag Pookie into Applications |

## Build from source

Pookie is written in C# with .NET 10 and MewUI. Install Git and the .NET 10 SDK. The first restore fetches the pinned MewUI revision and builds its packages locally

```sh
git clone https://github.com/freakdaniel/Pookie.git
cd Pookie
```

On Linux x64, first download the `libvlc-linux-x64` artifact from a successful **Build and package** run and extract its contents into `artifacts/libvlc-runtime/`. The build checks the archive and bundles libVLC with the player. [Linux runtime instructions](tools/libvlc-linux/README.md)

```sh
dotnet run --project src/Pookie.App
```

To run the .NET and JavaScript test suites, install Node.js 22 or newer and run `npm test`

## License

Pookie’s code is distributed under the BSD 3-Clause license. Bundled artwork, fonts and libraries have their own terms and credits. [License](LICENSE) · [Third-party notices](licenses/) · [Asset attributions](REUSE.toml)
