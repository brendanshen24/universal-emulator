# Native library for the Uno app: the NESBox core (libnesbox) + 7z + sound + pads, behind the C API in mboxpanels.cpp.
#   macOS, Apple silicon:          make                -> libmboxpanels.dylib
#   Windows x64, cross-built here: make TARGET=windows -> mboxpanels.dll  (needs zig; ZIG=path/to/zig)
# `make test` / `make TARGET=windows test` build and run a self-check of the C API (the Windows one under wine).

TARGET ?= mac
ZIG ?= zig
S := ../libnesbox/src
NES    := $(wildcard $(S)/nes/*.cpp $(S)/nes/core/*.cpp $(S)/nes/core/*/*.cpp)
SNES   := $(wildcard $(S)/snes/*.cpp $(S)/snes/core/*.cpp)
SEGA   := $(wildcard $(S)/sega/*.cpp $(S)/sega/core/*.cpp $(S)/sega/core/*/*.cpp)
GB     := $(wildcard $(S)/gb/*.cpp $(S)/gb/core/*.cpp $(S)/gb/core/*/*.cpp)
SEVENZ := $(S)/7z/7zDecoder.cpp $(wildcard $(S)/7z/core/*.c)

ifeq ($(TARGET),windows)
O := obj-win
CC := $(ZIG) cc -target x86_64-windows-gnu
CXX := $(ZIG) c++ -target x86_64-windows-gnu
LIB := mboxpanels.dll
TEST := selftest.exe
LIBFLAGS := -shared -static
SNES_PLATFORM :=
else
O := obj-mac
CC := clang -arch arm64 -mmacosx-version-min=11.0
CXX := clang++ -arch arm64 -mmacosx-version-min=11.0
LIB := libmboxpanels.dylib
TEST := selftest
LIBFLAGS := -dynamiclib -install_name @rpath/libmboxpanels.dylib -framework GameController -framework Foundation
SNES_PLATFORM := -DNESBOX_JS -include unistd.h # macOS lacks the CRT path helpers NESBOX_JS stubs; movie.cpp forgets unistd.h
PLATFORM_OBJ := $(O)/pad_mac.mm.o
SEVENZ_PLATFORM := -DS_OK=0 # 7zDecoder.cpp returns the Windows HRESULT name
CXXFLAGS_PLATFORM := -fno-strict-return # old core code falls off the end of non-void functions; Apple clang traps there
endif

o = $(patsubst $(S)/%,$(O)/core/%.o,$(1)) # core sources -> objects under $(O)/core
CORE := $(call o,$(S)/nesbox.cpp $(NES) $(SNES) $(SEGA) $(GB) $(SEVENZ))
CFLAGS   := -O2 -w
CXXFLAGS := -O2 -w -Wno-narrowing -std=gnu++11 -fvisibility=hidden $(CXXFLAGS_PLATFORM)

# Per-core defines, as in libnesbox/build/mingw/makefile.
$(call o,$(NES)):  CXXFLAGS += -DNETWORK -DLSB_FIRST
$(call o,$(S)/7z/7zDecoder.cpp): CXXFLAGS += $(SEVENZ_PLATFORM)
$(call o,$(SEGA)): CXXFLAGS += -DWITH_CZ80 -DWITH_MUSA
$(call o,$(GB)):   CXXFLAGS += -DFINAL_VERSION -DNO_PNG -DNO_LINK -DC_CORE -DNESBOX_JS
$(call o,$(SNES)): CXXFLAGS += -D__FLASH__ -DHAVE_STDINT_H -DHAVE_SYS_IOCTL_H -DUSE_VIDMODE_EXTENSION \
	-DUSE_DGA_EXTENSION -DHAVE_MKSTEMP -DNEW_COLOUR_BLENDING -DNOASM -DSPC700_SHUTDOWN -DMITSHM $(SNES_PLATFORM)

$(LIB): $(O)/mboxpanels.cpp.o $(PLATFORM_OBJ) $(CORE)
	$(CXX) -o $@ $^ $(LIBFLAGS)

$(TEST): $(O)/selftest.cpp.o $(O)/mboxpanels.cpp.o $(PLATFORM_OBJ) $(CORE)
	$(CXX) -o $@ $^ $(if $(PLATFORM_OBJ),-framework GameController -framework Foundation,-static)

test: $(TEST)
	$(if $(PLATFORM_OBJ),./$(TEST),wine64 $(TEST))

$(O)/%.cpp.o: %.cpp
	@mkdir -p $(dir $@)
	$(CXX) $(CXXFLAGS) -MMD -MP -c $< -o $@

$(O)/core/%.cpp.o: $(S)/%.cpp
	@mkdir -p $(dir $@)
	$(CXX) $(CXXFLAGS) -MMD -MP -c $< -o $@

$(O)/core/%.c.o: $(S)/%.c
	@mkdir -p $(dir $@)
	$(CC) $(CFLAGS) -MMD -MP -c $< -o $@

$(O)/%.mm.o: %.mm
	@mkdir -p $(dir $@)
	$(CXX) -O2 -fobjc-arc -c $< -o $@

clean:
	rm -rf obj-mac obj-win libmboxpanels.dylib mboxpanels.dll mboxpanels.lib selftest selftest.exe

.PHONY: test clean
-include $(shell find $(O) -name '*.d' 2>/dev/null)
