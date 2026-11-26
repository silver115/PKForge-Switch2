// pkf_names.c's encoding, on the build host: gcc -std=c11 -Wall -Werror tests/names_test.c pkf_names.c
#include <assert.h>
#include <stdio.h>
#include <string.h>
#include "../pkf_names.h"

static const char* encoded(const char* path) {
    static char out[512];
    assert(pkf_name_encode(path, out, sizeof out) == 0);
    return out;
}

int main(void) {
    // Plain names and the device prefix stay as they are.
    assert(!strcmp(encoded("sdmc:/switch/PKForge/a b&c.txt"), "sdmc:/switch/PKForge/a b&c.txt"));
    // A colon after the prefix, accents, kana and '%' are written as %XX.
    assert(!strcmp(encoded("sdmc:/x/Legends: Z-A-modified.sav"), "sdmc:/x/Legends%3A Z-A-modified.sav"));
    assert(!strcmp(encoded("sdmc:/x/\xC3\xA9%.txt"), "sdmc:/x/%C3%A9%25.txt"));
    assert(!strcmp(encoded("sdmc:/g9/0048 SV - \xE3\x83\x97 Snorlax.wc9"), "sdmc:/g9/0048 SV - %E3%83%97 Snorlax.wc9"));
    assert(!strcmp(encoded("/no/device/\xE3\x82\xA2?"), "/no/device/%E3%82%A2%3F"));
    // Decoding turns the SD card's names back; anything that is not an uppercase %XX stays.
    char name[64] = "%C3%A9%25 x%3A";
    assert(pkf_name_decode(name) && !strcmp(name, "\xC3\xA9% x:"));
    char odd[16] = "100%zz %4";
    assert(!pkf_name_decode(odd) && !strcmp(odd, "100%zz %4"));
    // Every byte but '/' round-trips.
    char all[256], out[1024], back[1024];
    for (int i = 1; i < 256; i++) all[i - 1] = (char)(i == '/' ? 'x' : i);
    all[255] = 0;
    assert(pkf_name_encode(all, out, sizeof out) == 0);
    strcpy(back, out);
    pkf_name_decode(back);
    assert(!strcmp(back, all));
    // A buffer too small is refused, not overrun.
    char tiny[8];
    assert(pkf_name_encode("sdmc:/\xE3\x82\xA2", tiny, sizeof tiny) == -1);
    puts("names ok");
    return 0;
}
