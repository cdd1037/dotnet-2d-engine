#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build-headless
cxx="${CXX:-g++}"
"$cxx" -std=c++17 -Wall -Wextra -Werror -O2 -fPIC -fvisibility=hidden -shared native/src/gal.cpp native/src/backend_headless.cpp -Inative/include -pthread -o build-headless/libgal.so
"$cxx" -std=c++17 -Wall -Wextra -Werror -O2 tests/native_tests.cpp -Inative/include -Lbuild-headless -lgal -pthread -Wl,-rpath,'$ORIGIN' -o build-headless/gal_native_tests
build-headless/gal_native_tests
"$cxx" -std=c++17 -Wall -Wextra -Werror -O2 tests/backend_contract_tests.cpp native/src/gal.cpp -Inative/include -Inative/src -pthread -o build-headless/gal_backend_contract_tests
build-headless/gal_backend_contract_tests
"${CC:-gcc}" -std=c11 -Wall -Wextra -Werror tests/c_consumer.c -Inative/include -Lbuild-headless -lgal -Wl,-rpath,'$ORIGIN' -o build-headless/gal_c_consumer
build-headless/gal_c_consumer
echo 'PASS C11 consumer ABI layouts/create/destroy'
