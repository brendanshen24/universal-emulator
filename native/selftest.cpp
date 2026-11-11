// Self-check for the native library's C API (what the app calls): runs a tiny hand-written NES program
// (red screen + 440 Hz tone) and checks the picture, the sound, save states, and loading a second game.
#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <vector>

struct Core;
extern "C" {
Core* nb_create(const char* name, int sampleRate);
int nb_load_rom(Core*, const unsigned char* data, int size);
void nb_info(Core*, int* type, int* width, int* height, int* framerate, int* pal);
const unsigned char* nb_tick(Core*, unsigned int player1, unsigned int player2);
int nb_sound(Core*, const short** samples);
int nb_save_state(Core*, const unsigned char** data);
int nb_load_state(Core*, const unsigned char* data, int size);
void nb_destroy(Core*);
}

#define CHECK(c) do { if (!(c)) { printf("FAIL line %d: %s\n", __LINE__, #c); return 1; } } while (0)

static std::vector<unsigned char> TestRom()
{
	const unsigned char header[16] = { 'N', 'E', 'S', 0x1A, 2, 1 }; // 32 KB code, 8 KB (blank) tiles, mapper 0
	const unsigned char code[] = {
		0x78, 0xD8, 0xA2, 0x40, 0x8E, 0x17, 0x40, 0xA2, 0xFF, 0x9A, 0xE8,  // sei cld, ldx #$40 stx $4017, ldx #$ff txs inx
		0x8E, 0x00, 0x20, 0x8E, 0x01, 0x20,                                // ppu off
		0x2C, 0x02, 0x20, 0x10, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB,        // wait two vblanks
		0xA9, 0x3F, 0x8D, 0x06, 0x20, 0xA9, 0x00, 0x8D, 0x06, 0x20,        // vram addr = $3F00
		0xA9, 0x16, 0x8D, 0x07, 0x20,                                      // backdrop colour = $16 (red)
		0xA9, 0x00, 0x8D, 0x06, 0x20, 0x8D, 0x06, 0x20,                    // vram addr = 0
		0xA9, 0x0A, 0x8D, 0x01, 0x20,                                      // background on
		0xA9, 0x01, 0x8D, 0x15, 0x40, 0xA9, 0xBF, 0x8D, 0x00, 0x40,        // pulse 1 on, constant volume 15
		0xA9, 0xFD, 0x8D, 0x02, 0x40, 0xA9, 0x00, 0x8D, 0x03, 0x40,        // period $0FD = ~440 Hz
	};
	std::vector<unsigned char> rom(header, header + 16);
	rom.resize(16 + 0x8000 + 0x2000, 0);
	unsigned char* prg = &rom[16];
	std::copy(code, code + sizeof code, prg);
	int jmp = sizeof code, rti = jmp + 3, at = 0x8000 + jmp;
	prg[jmp] = 0x4C, prg[jmp + 1] = at & 0xFF, prg[jmp + 2] = at >> 8; // jmp to itself
	prg[rti] = 0x40;
	const int vectors[] = { 0x8000 + rti, 0x8000, 0x8000 + rti }; // nmi, reset, irq
	for (int i = 0; i < 3; i++) prg[0x7FFA + i * 2] = vectors[i] & 0xFF, prg[0x7FFB + i * 2] = vectors[i] >> 8;
	return rom;
}

static int RunGame(int round)
{
	std::vector<unsigned char> rom = TestRom();
	CHECK(!nb_create("notes.txt", 48000)); // unknown extension: no emulator, as in the original
	Core* core = nb_create("selftest.nes", 48000);
	CHECK(core && nb_load_rom(core, rom.data(), (int)rom.size()));
	int type, width, height, framerate, pal;
	nb_info(core, &type, &width, &height, &framerate, &pal);
	CHECK(type == 2 && width == 256 && height == 240 && framerate == 60 && !pal); // libnesbox EmulatorType::NES

	const unsigned char* frame = nullptr;
	size_t samples = 0;
	int peak = 0;
	for (int i = 0; i < 60; i++)
	{
		frame = nb_tick(core, 0, 0);
		const short* sound;
		int n = nb_sound(core, &sound);
		samples += n;
		for (int j = 0; j < n; j++) peak = std::max(peak, abs(sound[j]));
	}
	unsigned px = ((const unsigned*)frame)[width * height / 2 + width / 2];
	int r = px >> 16 & 0xFF, g = px >> 8 & 0xFF, b = px & 0xFF;
	printf("round %d: centre pixel %02x%02x%02x, %zu samples in 1s, peak %d\n", round, r, g, b, samples, peak);
	CHECK(r > 120 && g < 100 && b < 100);       // red backdrop
	CHECK(samples > 90000 && samples < 102000); // ~48000 stereo frames per emulated second
	CHECK(peak > 1000);                         // the tone is audible

	const unsigned char* state;
	int size = nb_save_state(core, &state);
	std::vector<unsigned char> saved(state, state + size);
	CHECK(size > 0);
	for (int i = 0; i < 10; i++) nb_tick(core, 0, 0);
	CHECK(nb_load_state(core, saved.data(), (int)saved.size()));
	frame = nb_tick(core, 0, 0);
	CHECK(((const unsigned*)frame)[width * 120 + 128] == px);
	nb_destroy(core);
	return 0;
}

int main()
{
	// Two rounds: the second proves a game can be closed and another opened in the same process.
	for (int round = 1; round <= 2; round++)
		if (RunGame(round)) return 1;
	puts("selftest OK");
	return 0;
}
