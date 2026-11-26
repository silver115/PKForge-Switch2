#---------------------------------------------------------------------------------
# Mono interpreter smoke test for Switch 2 (phase 0, Task 3): mono-nx's interpreter host
# (/deps/mono-nx/native, at 285b277d) linked against our Release Mono build for libounce.
# .NET 10 by default; PKF_RUNTIME_DIR=/deps/dotnet_runtime PKF_RUNTIME_TFM=net9.0 builds the .NET 9 one.
# Based on ounce-devkit's examples/gles-triangle/Makefile and mono-nx's native/interpreter/Makefile.
# Run build.sh rather than this file directly.
#---------------------------------------------------------------------------------
.SUFFIXES:

ifeq ($(strip $(DEVKITPRO)),)
$(error "Please set DEVKITPRO in your environment. export DEVKITPRO=<path to>/devkitpro")
endif

TOPDIR ?= $(CURDIR)
include $(DEVKITPRO)/libounce/switch2_rules

PKF_DEPS	?=	/deps
MONO_NX		:=	$(PKF_DEPS)/mono-nx/native
PKF_RUNTIME_DIR	?=	$(PKF_DEPS)/dotnet_runtime10
PKF_RUNTIME_TFM	?=	net10.0
RT			:=	$(PKF_RUNTIME_DIR)/artifacts
NATIVE		:=	$(RT)/bin/native/$(PKF_RUNTIME_TFM)-libnx-Release-arm64
ICU			:=	$(PKF_DEPS)/mono-nx/icu/libnx

TARGET		:=	mono-smoke
BUILD		:=	build/obj
# The shared code is the host's vendored copy: upstream's dl_shim lists .NET 9's System.Native by hand.
SOURCES		:=	$(MONO_NX)/interpreter/source ../../host/mono-nx ../../host/mono-nx/third_party/ini ../../pkfhost
DATA		:=
INCLUDES	:=	../../host/mono-nx ../../pkfhost

APP_TITLE	:=	PKForge Mono smoke test
APP_AUTHOR	:=	PKForge (mono-nx host)
APP_VERSION	:=	0.0.1

ARCH	:=	$(SWITCH2_CPU) -mtp=soft -fPIE

CFLAGS	:=	-g -Wall -O2 -ffunction-sections -fno-omit-frame-pointer $(ARCH) $(DEFINES)
CFLAGS	+=	$(INCLUDE) -D__SWITCH__ \
			-I$(RT)/bin/mono/libnx.arm64.Release/include/mono-2.0 \
			-I$(ICU)/include \
			-DU_DISABLE_RENAMING=1

CXXFLAGS	:= $(CFLAGS) -fno-rtti -fno-exceptions -std=c++17
ASFLAGS	:=	-g $(ARCH)
LDFLAGS	=	-specs=$(DEVKITPRO)/libnx/switch.specs -g $(ARCH) -Wl,-Map,$(notdir $*.map)

LIBS	:=	\
			$(NATIVE)/libSystem.IO.Compression.Native.a \
			$(NATIVE)/libSystem.Globalization.Native.a \
			$(NATIVE)/libSystem.Native.a \
			$(wildcard $(NATIVE)/libbrotlienc.a $(NATIVE)/libbrotlidec.a $(NATIVE)/libbrotlicommon.a) \
			$(ICU)/lib/libicui18n.a \
			$(ICU)/lib/libicuuc.a \
			$(ICU)/lib/libicudata.a \
			$(RT)/obj/mono/libnx.arm64.Release/out/lib/libmonosgen-2.0.a \
			$(RT)/obj/mono/libnx.arm64.Release/mono/mini/libmono-component-debugger-static.a \
			$(RT)/obj/mono/libnx.arm64.Release/out/lib/libmono-component-diagnostics_tracing-stub-static.a \
			$(RT)/obj/mono/libnx.arm64.Release/out/lib/libmono-component-hot_reload-stub-static.a \
			$(RT)/obj/mono/libnx.arm64.Release/out/lib/libmono-component-marshal-ilgen-static.a \
			$(firstword $(wildcard $(NATIVE)/libz.a $(RT)/obj/mono/libnx.arm64.Release/_deps/fetchzlibng-build/libz.a)) \
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
export NROFLAGS	+=	--nacp=$(CURDIR)/$(TARGET).nacp

.PHONY: $(BUILD) clean all

all: $(BUILD)

$(BUILD):
	@mkdir -p $@
	@$(MAKE) --no-print-directory -C $(BUILD) -f $(CURDIR)/Makefile

clean:
	@rm -fr build $(TARGET).nro $(TARGET).nacp $(TARGET).elf

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
