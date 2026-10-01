#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build-headless
cxx="${CXX:-g++}"
"$cxx" -std=c++17 -Wall -Wextra -Werror -O2 -fPIC -fvisibility=hidden -shared native/src/gal.cpp native/src/backend_headless.cpp native/src/audio_stub.cpp native/src/physics_stub.cpp -Inative/include -pthread -o build-headless/libgal.so
"$cxx" -std=c++17 -Wall -Wextra -Werror -O2 tests/native_tests.cpp -Inative/include -Lbuild-headless -lgal -pthread -Wl,-rpath,'$ORIGIN' -o build-headless/gal_native_tests
build-headless/gal_native_tests
"$cxx" -std=c++17 -Wall -Wextra -Werror -O2 tests/backend_contract_tests.cpp native/src/gal.cpp native/src/audio_stub.cpp native/src/physics_stub.cpp -Inative/include -Inative/src -pthread -o build-headless/gal_backend_contract_tests
build-headless/gal_backend_contract_tests
"${CC:-gcc}" -std=c11 -Wall -Wextra -Werror tests/c_consumer.c -Inative/include -Lbuild-headless -lgal -Wl,-rpath,'$ORIGIN' -o build-headless/gal_c_consumer
build-headless/gal_c_consumer
echo 'PASS C11 consumer ABI layouts/create/destroy'

"$cxx" -std=c++17 -Wall -Wextra -Werror -O2 tests/input_state_tests.cpp -Inative/include -Inative/src -o build-headless/gal_input_state_tests
build-headless/gal_input_state_tests
