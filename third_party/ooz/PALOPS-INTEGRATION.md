# PalOps Web ooz integration

This directory contains the decoder artifact and complete corresponding source integrated into PalOps Web.

- Upstream package: `ooz-wasm` 2.0.0
- Upstream repository: `https://github.com/SnosMe/ooz-wasm`
- License: GPL-3.0-or-later
- Embedded runtime artifact: `src/PalOps.Web/SaveGames/Binary/Native/ooz.wasm`
- Runtime entry point: `Kraken_Decompress`
- Host: Wasmtime for .NET

The `.wasm` bytes were extracted without modification from the base64 `SINGLE_FILE` payload in the published `ooz-wasm` 2.0.0 npm package. The published wrapper and the source snapshot both identify version 2.0.0, and their `index.js` files are byte-identical.

Included material:

- `source/`: complete CMake/C++/JavaScript source needed to rebuild the decoder;
- `ooz-wasm-2.0.0-source.tar.gz`: matching source snapshot archive;
- `ooz-wasm-2.0.0.tgz`: original published npm package;
- `index.js`, `index.d.ts`, `package.json`: published wrapper and metadata;
- `LICENSE`, `README.upstream.md`: GPL license and upstream build instructions;
- `SHA256SUMS`: integrity values for the retained artifacts and embedded Wasm file.

PalOps Web supplies the two Emscripten imports used by this build (`emscripten_resize_heap` and `emscripten_memcpy_js`) through Wasmtime. It allocates compressed and output buffers, invokes `Kraken_Decompress`, validates the exact decoded length, copies the bytes back into managed memory, and frees both WebAssembly allocations.
