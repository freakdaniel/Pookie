CdmAbi.cs translates the public Chromium Host/CDM v10 ABI declarations into C# layouts and unmanaged function pointers. BSD-3-Clause notice: ABI-LICENSE.

Declaration source: Kodi InputStream Adaptive commit e14d199a5ed7224d1c42a36619ab0a67d3d8f3f6, content_decryption_module.h:
https://github.com/xbmc/inputstream.adaptive/tree/e14d199a5ed7224d1c42a36619ab0a67d3d8f3f6/lib/cdm/cdm/media/cdm/api

No Kodi implementation code or proprietary Widevine binary is bundled. Our host is implemented in C#; the installed Google CDM remains native. This interop targets 64-bit x64/ARM64 instance-call conventions; Linux x64 has been tested against the real CDM. Windows/macOS still require runtime validation.
