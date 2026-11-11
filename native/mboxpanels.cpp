// Native half of the rebuild. The original NesboxPanels.dll embedded the NESBox core (libnesbox) behind the
// MBoxPanels.EmulatorPanel/SevenZipWrapper WinRT classes; this exposes the same core as a C API the Uno app
// calls through P/Invoke, plus sound output (XAudio2 in the original, miniaudio here) and Xbox-pad input
// (GameController.dll/XInput in the original; pad_mac.mm on macOS).
#include "../libnesbox/include/nesbox.h"
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

#define MINIAUDIO_IMPLEMENTATION
#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_NODE_GRAPH
#define MA_NO_ENGINE
#include "miniaudio.h"

#ifdef _WIN32
#define API extern "C" __declspec(dllexport)
#else
#define API extern "C" __attribute__((visibility("default")))
#endif

static std::string Utf8(const std::wstring& w) // wchar_t is UTF-16 on Windows, UTF-32 elsewhere
{
	std::string out;
	for (size_t i = 0; i < w.size(); i++)
	{
		uint32_t c = (uint32_t)w[i];
		if (c >= 0xD800 && c < 0xDC00 && i + 1 < w.size()) c = 0x10000 + ((c - 0xD800) << 10) + ((uint32_t)w[++i] - 0xDC00);
		if (c < 0x80) out += (char)c;
		else if (c < 0x800) out += (char)(0xC0 | c >> 6), out += (char)(0x80 | (c & 0x3F));
		else if (c < 0x10000) out += (char)(0xE0 | c >> 12), out += (char)(0x80 | (c >> 6 & 0x3F)), out += (char)(0x80 | (c & 0x3F));
		else out += (char)(0xF0 | c >> 18), out += (char)(0x80 | (c >> 12 & 0x3F)), out += (char)(0x80 | (c >> 6 & 0x3F)), out += (char)(0x80 | (c & 0x3F));
	}
	return out;
}

static std::wstring Wide(const char* s) // UTF-8 -> wchar_t
{
	std::wstring out;
	for (const unsigned char* p = (const unsigned char*)s; *p;)
	{
		uint32_t c = *p++;
		int extra = c >= 0xF0 ? 3 : c >= 0xE0 ? 2 : c >= 0xC0 ? 1 : 0;
		c &= extra == 3 ? 0x07 : extra == 2 ? 0x0F : extra == 1 ? 0x1F : 0x7F;
		while (extra-- && (*p & 0xC0) == 0x80) c = c << 6 | (*p++ & 0x3F);
		if (sizeof(wchar_t) == 2 && c >= 0x10000) out += (wchar_t)(0xD800 + ((c - 0x10000) >> 10)), out += (wchar_t)(0xDC00 + ((c - 0x10000) & 0x3FF));
		else out += (wchar_t)c;
	}
	return out;
}

// ---------------------------------------------------------------- emulator core

struct Core
{
	EmulatorPtr emu;
	SoundBuffer sound;
	StateBuffer state;
};

// The name only picks the console (by extension) and region (by "(E)"-style tags), as in the original.
API Core* nb_create(const char* utf8Name, int sampleRate)
{
	EmulatorPtr emu = Emulator::CreateByRomFile(Wide(utf8Name), sampleRate);
	if (!emu) return nullptr;
	Core* core = new Core();
	core->emu = std::move(emu);
	return core;
}

API int nb_load_rom(Core* core, const unsigned char* data, int size)
{
	try { return core->emu->LoadRom(RomBuffer(data, data + size)) ? 1 : 0; }
	catch (...) { return 0; }
}

// libnesbox EmulatorType (SEGA, SNES, NES, GBA, GBX); width/height of the frame buffer; framerate; 1 if PAL.
API void nb_info(Core* core, int* type, int* width, int* height, int* framerate, int* pal)
{
	const RomInfo& info = core->emu->GetRomInfo();
	*type = info.emulatorType;
	*width = info.width;
	*height = info.height;
	*framerate = info.framerate;
	*pal = info.isPal() ? 1 : 0;
}

// Keys use the ControlKey/EmulatorKey bit order (Up, Down, Left, Right, Start, Select, Mode, A, B, C, L, R, X, Y, Z).
// Returns the frame: width*height 32-bit pixels, 0x00RRGGBB (B,G,R,x in memory).
API const unsigned char* nb_tick(Core* core, unsigned int player1, unsigned int player2)
{
	EmulatorKeyBitset p1, p2;
	p1.SetKeys(player1);
	p2.SetKeys(player2);
	return core->emu->Tick(p1, p2);
}

// Interleaved stereo 16-bit samples produced since the last call; valid until the next call.
API int nb_sound(Core* core, const short** samples)
{
	core->sound = core->emu->GetSoundBuffer();
	*samples = core->sound.data();
	return (int)core->sound.size();
}

API int nb_save_state(Core* core, const unsigned char** data)
{
	core->state = core->emu->SaveState();
	*data = core->state.data();
	return (int)core->state.size();
}

API int nb_load_state(Core* core, const unsigned char* data, int size)
{
	try { core->emu->LoadState(StateBuffer(data, data + size)); return 1; }
	catch (...) { return 0; }
}

API void nb_destroy(Core* core) { delete core; }

// ---------------------------------------------------------------- 7z archives (SevenZipWrapper)

struct Archive
{
	SevenZDecoder decoder;
	std::string names;
	SevenZBuffer extracted;
	Archive(const SevenZBuffer& b) : decoder(b) {}
};

API Archive* nb_7z_open(const unsigned char* data, int size)
{
	try { return new Archive(SevenZBuffer(data, data + size)); }
	catch (...) { return nullptr; }
}

// Entry names, UTF-8, separated by '\n'; valid until the archive is closed.
API const char* nb_7z_list(Archive* a)
{
	a->names.clear();
	for (auto& name : a->decoder.list()) a->names += Utf8(name) + "\n";
	return a->names.c_str();
}

API int nb_7z_extract(Archive* a, const char* utf8Name, const unsigned char** data)
{
	a->extracted.clear();
	a->decoder.extract(Wide(utf8Name), a->extracted);
	*data = a->extracted.data();
	return (int)a->extracted.size();
}

API void nb_7z_close(Archive* a) { delete a; }

// ---------------------------------------------------------------- sound output

static ma_device g_device;
static bool g_deviceOpen;
static std::mutex g_audioLock;
static std::vector<short> g_audio; // queued interleaved stereo samples
static size_t g_audioRead;

static void AudioCallback(ma_device*, void* out, const void*, ma_uint32 frames)
{
	short* dst = (short*)out;
	size_t want = frames * 2, got = 0;
	{
		std::lock_guard<std::mutex> lock(g_audioLock);
		got = std::min(want, g_audio.size() - g_audioRead);
		memcpy(dst, g_audio.data() + g_audioRead, got * sizeof(short));
		g_audioRead += got;
		if (g_audioRead == g_audio.size()) g_audio.clear(), g_audioRead = 0;
	}
	memset(dst + got, 0, (want - got) * sizeof(short)); // underrun: silence
}

API int nb_audio_open(int sampleRate)
{
	if (g_deviceOpen) return 1;
	ma_device_config config = ma_device_config_init(ma_device_type_playback);
	config.playback.format = ma_format_s16;
	config.playback.channels = 2;
	config.sampleRate = sampleRate;
	config.dataCallback = AudioCallback;
	if (ma_device_init(nullptr, &config, &g_device) != MA_SUCCESS) return 0;
	if (ma_device_start(&g_device) != MA_SUCCESS) { ma_device_uninit(&g_device); return 0; }
	return g_deviceOpen = true;
}

API void nb_audio_write(const short* samples, int count)
{
	std::lock_guard<std::mutex> lock(g_audioLock);
	g_audio.insert(g_audio.end(), samples, samples + count);
}

// Stereo frames still waiting to play.
API int nb_audio_queued()
{
	std::lock_guard<std::mutex> lock(g_audioLock);
	return (int)(g_audio.size() - g_audioRead) / 2;
}

API void nb_audio_clear()
{
	std::lock_guard<std::mutex> lock(g_audioLock);
	g_audio.clear();
	g_audioRead = 0;
}

// ---------------------------------------------------------------- Xbox-style pads (Windows; macOS is pad_mac.mm)

struct Pad { unsigned short buttons; short thumbLX, thumbLY; unsigned char leftTrigger, rightTrigger; };

#ifdef _WIN32
#include <windows.h>
#include <xinput.h>

typedef DWORD(WINAPI* XInputGetStateFn)(DWORD, XINPUT_STATE*);

API int nb_pad_state(int index, Pad* pad)
{
	static XInputGetStateFn get = [] {
		HMODULE dll = LoadLibraryW(L"xinput1_4.dll");
		if (!dll) dll = LoadLibraryW(L"xinput9_1_0.dll");
		return dll ? (XInputGetStateFn)GetProcAddress(dll, "XInputGetState") : nullptr;
	}();
	XINPUT_STATE s = {};
	if (!get || get(index, &s) != ERROR_SUCCESS) return 0;
	pad->buttons = s.Gamepad.wButtons;
	pad->thumbLX = s.Gamepad.sThumbLX;
	pad->thumbLY = s.Gamepad.sThumbLY;
	pad->leftTrigger = s.Gamepad.bLeftTrigger;
	pad->rightTrigger = s.Gamepad.bRightTrigger;
	return 1;
}
#endif
