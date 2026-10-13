// Exult's ucexpr.cc defines virtual methods of Uc_call_expression as
// `inline` in the .cc; GCC still emits them, MSVC doesn't, so other files'
// vtables can't link. Compile it here with its headers read first and
// `inline` defined away for its own body only.
#include "ucexpr.h"

#include "basic_block.h"
#include "ignore_unused_variable_warning.h"
#include "opcodes.h"
#include "ucclass.h"
#include "ucfun.h"
#include "ucloc.h"
#include "ucsym.h"

#include <cstdio>

#define inline
#include "ucexpr.cc"
#undef inline
