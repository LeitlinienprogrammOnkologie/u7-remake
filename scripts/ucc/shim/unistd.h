// Stand-in for POSIX <unistd.h> so Exult's UCC builds with MSVC: it only
// needs getopt (and flex's generated lexer, isatty).
#pragma once

#include <io.h>

#ifdef __cplusplus
extern "C" {
#endif

extern char* optarg;
extern int   optind;
extern int   opterr;
extern int   optopt;

int getopt(int argc, char* const argv[], const char* optstring);

#ifdef __cplusplus
}
#endif
