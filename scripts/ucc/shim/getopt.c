/* Minimal POSIX getopt for UCC under MSVC: short options, "x:" arguments
 * attached or separate, clustered flags, "--" ends the options. */
#include <stdio.h>
#include <string.h>

char* optarg = NULL;
int   optind = 1;
int   opterr = 1;
int   optopt = 0;

static int next_char = 0;

int getopt(int argc, char* const argv[], const char* optstring) {
	optarg = NULL;
	if (next_char == 0) {
		if (optind >= argc || argv[optind][0] != '-' || argv[optind][1] == '\0') {
			return -1;
		}
		if (strcmp(argv[optind], "--") == 0) {
			optind++;
			return -1;
		}
		next_char = 1;
	}
	const char* arg = argv[optind];
	const int   c   = (unsigned char)arg[next_char++];
	const char* spec = strchr(optstring, c);
	if (spec == NULL || c == ':') {
		optopt = c;
		if (opterr) {
			fprintf(stderr, "%s: unknown option -%c\n", argv[0], c);
		}
		if (arg[next_char] == '\0') {
			optind++;
			next_char = 0;
		}
		return '?';
	}
	if (spec[1] == ':') {
		if (arg[next_char] != '\0') {
			optarg = (char*)&arg[next_char];
			optind++;
		} else if (optind + 1 < argc) {
			optarg = argv[optind + 1];
			optind += 2;
		} else {
			optopt = c;
			optind++;
			next_char = 0;
			return '?';
		}
		next_char = 0;
		return c;
	}
	if (arg[next_char] == '\0') {
		optind++;
		next_char = 0;
	}
	return c;
}
