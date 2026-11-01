# Voxa

**Real-time subtitles and translation for anything you hear on your PC.** Voxa listens to what is playing (or to your
microphone), turns speech into text and shows it translated as a transparent overlay at the bottom of the screen.
Speech recognition and translation run **locally** on your computer — no account, no cloud required.

[![Download Voxa for Windows](https://img.shields.io/github/v/release/drtilp/Voxa?label=Download%20for%20Windows&logo=windows&style=for-the-badge&color=3f3f46)](https://github.com/drtilp/Voxa/releases/latest/download/Voxa-Setup.exe)

[All releases and release notes](https://github.com/drtilp/Voxa/releases) · Windows 10/11 64-bit ·

## Install

1. Download **Voxa-Setup.exe** (button above) and run it.
2. Windows may show *"Windows protected your PC"* — the installer is not code-signed yet. Click **More info → Run anyway**.
3. Pick the install folder (default *Program Files*), the models and whether you want a desktop icon or autostart.
   Voxa needs the **.NET 10 Desktop Runtime**; when it is missing, the installer downloads it from Microsoft
   (about 60 MB) and installs it.
4. Voxa starts in the system tray (next to the clock). Left-click the tray icon to open it.

## Features

- Transparent, click-through overlay with the translation
- Source: what is playing on the PC (WASAPI loopback) or the default microphone
- Speech recognition: [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) with NVIDIA Parakeet or OpenAI Whisper, on the CPU
- Translation: Hy-MT2 on the GPU via [LLamaSharp](https://github.com/SciSharp/LLamaSharp) (Vulkan), DeepL or Azure Translator
- Runs in the system tray; model, source and language can be switched from the tray menu

## License

[MIT](LICENSE.txt). The models have their own licenses (Parakeet: CC-BY-4.0, Whisper: MIT, Hy-MT2: Apache 2.0, Silero VAD: MIT).
