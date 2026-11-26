// Names on the SD card (pkf_names.c): the reversible %XX form for bytes the console's file layer refuses.
#pragma once
#include <stdbool.h>
#include <stddef.h>

int  pkf_name_encode(const char* in, char* out, size_t size);  // 0, or -1 when out is too small
bool pkf_name_decode(char* name);                               // in place; true when something was decoded
