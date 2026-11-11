// macOS side of nb_pad_state: Apple's GameController framework reported in XInput terms,
// so the app reads Xbox, PlayStation and Switch pads the way the original read XInput.
#import <GameController/GameController.h>

struct Pad { unsigned short buttons; short thumbLX, thumbLY; unsigned char leftTrigger, rightTrigger; };

extern "C" __attribute__((visibility("default"))) int nb_pad_state(int index, Pad* pad)
{
	@autoreleasepool
	{
		NSArray<GCController*>* pads = GCController.controllers;
		if (index < 0 || index >= (int)pads.count) return 0;
		GCExtendedGamepad* g = pads[index].extendedGamepad;
		if (!g) return 0;
		struct { GCControllerButtonInput* input; unsigned short bit; } map[] = {
			{ g.dpad.up, 0x0001 }, { g.dpad.down, 0x0002 }, { g.dpad.left, 0x0004 }, { g.dpad.right, 0x0008 },
			{ g.buttonMenu, 0x0010 }, { g.buttonOptions, 0x0020 },
			{ g.leftThumbstickButton, 0x0040 }, { g.rightThumbstickButton, 0x0080 },
			{ g.leftShoulder, 0x0100 }, { g.rightShoulder, 0x0200 },
			{ g.buttonA, 0x1000 }, { g.buttonB, 0x2000 }, { g.buttonX, 0x4000 }, { g.buttonY, 0x8000 },
		};
		pad->buttons = 0;
		for (auto& m : map)
			if (m.input.pressed) pad->buttons |= m.bit;
		pad->thumbLX = (short)(g.leftThumbstick.xAxis.value * 32767);
		pad->thumbLY = (short)(g.leftThumbstick.yAxis.value * 32767);
		pad->leftTrigger = (unsigned char)(g.leftTrigger.value * 255);
		pad->rightTrigger = (unsigned char)(g.rightTrigger.value * 255);
		return 1;
	}
}
