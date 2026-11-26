#---------------------------------------------------------------------------------
# PKForge for Switch 2, phase 0 host (plan Task 6): source/main.c, ../pkfhost and mono-nx's shared code
# (mono-nx/, vendored) linked against the Release Mono fork for libounce. Run build.sh, which stages the IL
# in build/romfs first:
#   sh switch2/host/build.sh        Mono interpreter
#   sh switch2/host/build.sh aot    full AOT (make AOT=1, with build/linked/*.o and build/aot/mono_symbols.h)
# Based on switch2/runtime/interp-smoke/Makefile and mono-nx's native/aot/Makefile.
#---------------------------------------------------------------------------------
.SUFFIXES:

ifeq ($(strip $(DEVKITPRO)),)
$(error "Please set DEVKITPRO in your environment. export DEVKITPRO=<path to>/devkitpro")
endif

TOPDIR ?= $(CURDIR)
include $(DEVKITPRO)/libounce/switch2_rules

PKF_DEPS		?=	/deps
PKF_RUNTIME_DIR	?=	$(PKF_DEPS)/dotnet_runtime10
PKF_RUNTIME_TFM	?=	net10.0
RT				:=	$(PKF_RUNTIME_DIR)/artifacts
MONO			:=	$(RT)/obj/mono/libnx.arm64.Release
NATIVE			:=	$(RT)/bin/native/$(PKF_RUNTIME_TFM)-libnx-Release-arm64
ICU				:=	$(PKF_DEPS)/mono-nx/icu/libnx

TARGET		:=	PKForge
BUILD		:=	build/obj
SOURCES		:=	source mono-nx mono-nx/third_party/ini ../pkfhost
DATA		:=
INCLUDES	:=	mono-nx ../pkfhost
ROMFS		:=	build/romfs

APP_TITLE	=	PKForge
APP_AUTHOR	:=	PKForge
APP_VERSION	:=	0.1.1

ARCH	:=	$(SWITCH2_CPU) -mtp=soft -fPIE

# Runtime mode (see source/main.c): AOT=1 links the AOT objects, JIT=1 lets Mono JIT-compile.
ifeq ($(AOT),1)
DEFINES		:=	-DPKF_AOT=1
INCLUDES	+=	build/aot
DEBUGGER	:=	$(MONO)/out/lib/libmono-component-debugger-stub-static.a
AOT_OBJS	:=	$(wildcard $(TOPDIR)/build/linked/*.o)
else
DEFINES		:=
DEBUGGER	:=	$(MONO)/mono/mini/libmono-component-debugger-static.a
AOT_OBJS	:=
endif
ifeq ($(JIT),1)
DEFINES		+=	-DPKF_JIT=1
endif
ifeq ($(AOT)-$(JIT),1-)
# Full AOT: icalls and P/Invokes into the host's libraries are linked directly (build.sh), so mono-nx's
# dl_shim is not registered; a P/Invoke into any other library fails at run time.
DEFINES		+=	-DDLSHIM_DISABLE=1
endif
ifeq ($(TRACE),1)
DEFINES		+=	-DPKF_TRACE=1
endif
MODE_NAME	:=	$(if $(filter 1,$(AOT)),AOT$(if $(filter 1,$(JIT)),+JIT),$(if $(filter 1,$(JIT)),JIT,interpreter))$(if $(filter 1,$(TRACE)), trace)

CFLAGS	:=	-g -Wall -O2 -ffunction-sections -fno-omit-frame-pointer $(ARCH) $(DEFINES)
CFLAGS	+=	$(INCLUDE) -D__SWITCH__ \
			-I$(RT)/bin/mono/libnx.arm64.Release/include/mono-2.0 \
			-I$(ICU)/include \
			-DU_DISABLE_RENAMING=1

CXXFLAGS	:= $(CFLAGS) -fno-rtti -fno-exceptions -std=c++17
ASFLAGS	:=	-g $(ARCH)
LDFLAGS	=	-specs=$(DEVKITPRO)/libnx/switch.specs -g $(ARCH) -Wl,-Map,$(notdir $*.map) \
			-Wl,--wrap=SystemNative_Socket -Wl,--wrap=SystemNative_GetHostEntryForName

LIBS	:=	$(AOT_OBJS) \
			$(PKF_DEPS)/skia/out/libnx/switch/libSkiaSharp.a \
			$(NATIVE)/libSystem.IO.Compression.Native.a \
			$(NATIVE)/libSystem.Globalization.Native.a \
			$(NATIVE)/libSystem.Native.a \
			$(NATIVE)/libbrotlienc.a \
			$(NATIVE)/libbrotlidec.a \
			$(NATIVE)/libbrotlicommon.a \
			$(ICU)/lib/libicui18n.a \
			$(ICU)/lib/libicuuc.a \
			$(ICU)/lib/libicudata.a \
			$(MONO)/out/lib/libmonosgen-2.0.a \
			$(DEBUGGER) \
			$(MONO)/out/lib/libmono-component-diagnostics_tracing-stub-static.a \
			$(MONO)/out/lib/libmono-component-hot_reload-stub-static.a \
			$(MONO)/out/lib/libmono-component-marshal-ilgen-static.a \
			$(NATIVE)/libz.a \
			-pthread -lkaihen -lnx -lm -lstdc++

LIBDIRS	:= $(PORTLIBS) $(LIBNX)

# SOURCES and INCLUDES may be absolute paths here.
pathof = $(if $(filter /%,$(1)),$(1),$(CURDIR)/$(1))

#---------------------------------------------------------------------------------
ifneq ($(notdir $(BUILD)),$(notdir $(CURDIR)))
#---------------------------------------------------------------------------------

export OUTPUT	:=	$(CURDIR)/$(TARGET)
export TOPDIR	:=	$(CURDIR)
export VPATH	:=	$(foreach dir,$(SOURCES),$(call pathof,$(dir)))
export DEPSDIR	:=	$(CURDIR)/$(BUILD)

CFILES		:=	$(foreach dir,$(SOURCES),$(notdir $(wildcard $(dir)/*.c)))
export LD	:=	$(CC)
export OFILES	:=	$(CFILES:.c=.o)
export INCLUDE	:=	$(foreach dir,$(INCLUDES),-I$(call pathof,$(dir))) \
			$(foreach dir,$(LIBDIRS),-I$(dir)/include) \
			-I$(CURDIR)/$(BUILD)
export LIBPATHS	:=	$(foreach dir,$(LIBDIRS),-L$(dir)/lib)
export NROFLAGS	+=	--nacp=$(CURDIR)/$(TARGET).nacp --romfsdir=$(CURDIR)/$(ROMFS) --icon=$(CURDIR)/icon.jpg

.PHONY: $(BUILD) clean all

all: $(BUILD)

$(BUILD):
	@mkdir -p $@
	@$(MAKE) --no-print-directory -C $(BUILD) -f $(CURDIR)/Makefile

# Keeps build/romfs and the AOT outputs, which build.sh makes.
clean:
	@rm -fr $(BUILD) $(TARGET).nro $(TARGET).nacp $(TARGET).elf

#---------------------------------------------------------------------------------
else
.PHONY:	all

DEPENDS	:=	$(OFILES:.o=.d)

all	:	$(OUTPUT).nro
$(OUTPUT).nro	:	$(OUTPUT).elf $(OUTPUT).nacp
$(OUTPUT).elf	:	$(OFILES)

-include $(DEPENDS)

#---------------------------------------------------------------------------------
endif
#---------------------------------------------------------------------------------
