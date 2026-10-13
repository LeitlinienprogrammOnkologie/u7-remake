# Exult's usecode compiler (UCC) from the untracked Exult 1.12.1 source, for
# the remake's mods (mods/<name>/src -> mods/<name>/data/usecode).
# Built by scripts/ucc/build.ps1 with MSVC and WinFlexBison.
cmake_minimum_required(VERSION 3.20)
project(ucc C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(EXULT "${CMAKE_CURRENT_SOURCE_DIR}/../../exult/exult-1.12.1" CACHE PATH "Exult source")
set(UCC "${EXULT}/tools/compiler")

find_program(BISON NAMES win_bison bison REQUIRED)
find_program(FLEX NAMES win_flex flex REQUIRED)

add_custom_command(
	OUTPUT ucparse.cc ucparse.h
	COMMAND "${BISON}" -Wno-yacc --defines=ucparse.h -o ucparse.cc "${UCC}/ucparse.yy"
	DEPENDS "${UCC}/ucparse.yy")
add_custom_command(
	OUTPUT uclex.cc
	COMMAND "${FLEX}" --wincompat -o uclex.cc "${UCC}/uclex.ll"
	DEPENDS "${UCC}/uclex.ll" ucparse.h)

add_executable(ucc
	"${CMAKE_CURRENT_BINARY_DIR}/ucparse.cc"
	"${CMAKE_CURRENT_BINARY_DIR}/uclex.cc"
	"${UCC}/ucmain.cc"
	"${UCC}/ucclass.cc"
	shim/ucexpr_msvc.cc
	"${UCC}/ucfun.cc"
	"${UCC}/ucloc.cc"
	"${UCC}/ucstmt.cc"
	"${UCC}/ucsym.cc"
	"${EXULT}/usecode/ucsymtbl.cc"
	shim/getopt.c)
target_include_directories(ucc PRIVATE
	"${CMAKE_CURRENT_BINARY_DIR}"
	shim
	"${UCC}"
	"${EXULT}/headers"
	"${EXULT}/usecode"
	"${EXULT}/files"
	"${EXULT}")
target_compile_definitions(ucc PRIVATE _CRT_SECURE_NO_WARNINGS NOMINMAX)
if(MSVC)
	target_compile_options(ucc PRIVATE /utf-8 /permissive- /Zc:__cplusplus /W1 /wd4005)
endif()
