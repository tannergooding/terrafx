# Terminology and reading guide

Section 1 defines how to read the specifications. Sections 2-5 are an informative terminology reference; they add no requirements or alternative contracts. Proper names such as .NET, Vulkan, PipeWire, and GGX are not assigned invented acronym expansions.

## 1. Reading conventions

Requirements are normative by default, including tables, API declarations, algorithms, limits/defaults, and required acceptance observations. Informative material is marked by `(informative)` in a chapter/section heading or by a block beginning **Informative rationale:** or **Informative example:**. Such material explains or illustrates a contract; it cannot add, weaken, or override one. A mathematical derivation that establishes a required bound is normative unless explicitly separated as rationale.

[00](00-overview-and-scope.md#3-document-ownership) names the owner of each contract. References apply the owner's requirements without redefining them. [30](30-public-api-review-surface.md) owns public signatures; the subsystem owner defines their behavior. If they disagree, resolve the specification defect before implementation. Acceptance fixtures state required observations, not optional examples. An explicitly nonbinding performance estimate differs from a required measurement procedure or named qualification target.

The numbered chapters are the default reading order. Cross-references provide detail and connect consumers to their foundations; they do not imply an assembly reference or require jumping between alternative specifications.

API means **application programming interface**: the types, members, and behavior exposed to a caller. API names, identifiers, enum values, file names, and symbolic expressions use inline code, such as `World`, `TryGet<T>`, `QueueFull`, `global.json`, and `local * parentWorld`. Ordinary prose and abbreviations remain ordinary text.

An abbreviation is expanded at its first explanatory use where practical; this glossary covers abbreviations in tables, headings, and external source names as well. Chapter numbers identify the authority for a contract, not pipeline stages. A reference to chapter [13](13-jobs-and-frame-pipeline.md) is distinct from simulation stage 5.

Shorthand such as `PhysicsWorld2D/3D` means the separately named `PhysicsWorld2D` and `PhysicsWorld3D` types; a slash is not part of either API name. Likewise, `Vector2/3/4` denotes `Vector2`, `Vector3`, and `Vector4`. A symbolic signature illustrates a contract; it is not necessarily a complete compilable declaration.

Component expressions use programming-style member access, such as `a.Y`, rather than an unexplained concatenation such as `aY`. Arithmetic operators are spaced. In mathematical expressions, `^` denotes exponentiation; in the PCG32 code expression, it denotes C# bitwise exclusive-or. `cross`, `dot`, `normalize`, and similar lowercase names denote the stated mathematical operations, not additional public methods. Exact API signatures use the owning type's documented names.

Mathematical intervals use `[` or `]` to mark an inclusive endpoint and `(` or `)` to mark an exclusive endpoint: `[a, b]`, `[a, b)`, `(a, b]`, or `(a, b)`. For counts, say "integer values in `[a, b]`" to distinguish the admitted integers from every real value in the interval. Write every two-sided numeric value/count bound as an interval. The finalized animation, physics, and input limits use finite closed bounds at both endpoints and are written with `[a, b]`; preserve existing explicit strict inequalities and half-open algorithm intervals as written. A bare `a..b` is not endpoint notation for a numeric bound; retain it only for explicitly defined schema/API shorthand, acceptance-ID or source citations, and discrete index/byte/phase ranges. In particular, `DigitalControl[2..4]` is schema cardinality shorthand for integer sequence lengths in `[2, 4]`, not a C# range or slice. Interval notation states endpoint inclusion, not floating-point admission: a finite-value contract rejects both infinities and NaN, while NaN is unordered and is not a member of a mathematical interval. State finite/NaN admission separately when it is part of the contract. C# `array[start..end]` is a zero-based range expression with an exclusive upper slice endpoint; it is not mathematical interval notation.

Limits/defaults are binding unless explicitly nonbinding examples or estimates. Unmeasured qualification targets remain requirements to measure and report; they are not claims of achieved performance. Acceptance IDs are implementation obligations, not evidence tests ran. Prefixes include EN (cross-engine), PH (physics), PF (platform), AU (audio), IN (input), NM (numerics), SP (spatial), AS (native asset adapters), BF (buffers/collections), BN (binary formats), and AR (API/roadmap review). [37](37-completeness-and-audit.md) records actual evidence/limits.

## 2. .NET, software, and deployment (informative)

| Term | Expansion / meaning |
|---|---|
| ABI | Application binary interface: calling conventions, layout, and binary linkage between compiled components. Matching value sizes does not establish API or ABI compatibility. |
| AOT / NativeAOT | Ahead-of-time compilation; .NET NativeAOT produces a native executable before execution rather than relying on runtime JIT compilation. |
| ASCII | American Standard Code for Information Interchange; a seven-bit character encoding. |
| BCL | Base class library: .NET's foundational library types and operations, including collections, numerics, memory, and tasks. |
| CI | Continuous integration: automated build and validation of revisions. |
| CLI | Command-line interface. |
| CLR | Common Language Runtime: .NET's managed execution environment. |
| COM | Context-dependent: Component Object Model at a Windows interop boundary; **center of mass** in physics. These are unrelated meanings. |
| DAG | Directed acyclic graph: directed dependencies with no cycle; permits topological ordering. |
| DFS / BFS | Depth-first search / breadth-first search: graph traversal orders. |
| DI | Dependency injection: supplying dependencies explicitly rather than finding them through global state. |
| DLL | Dynamic-link library: a separately loaded native or managed library. |
| EOF | End of file or stream. |
| GA | General availability: a released shipping version, distinct from a preview or release candidate. |
| GC | Garbage collection / garbage collector: reclamation of managed objects no longer reachable. |
| GUID / ID | Globally unique identifier / identifier. `Guid` is the .NET type; a runtime handle's ID is not automatically a persistent GUID. |
| UUID / RFC | Universally unique identifier / Request for Comments, the Internet standards publication series. Active built-in name identities use [10](10-binary-data-and-authoring-formats.md#versioned-name-uuid-recipe)'s explicitly versioned SHA-256 UUIDv8 recipe; UUIDv5/SHA-1 is legacy migration input. UUIDv8 supplies version/variant layout, not an implicit hashing algorithm or authenticity guarantee. Formats use canonical RFC UUID bytes rather than CLR `Guid` memory order. |
| I/O | Input/output, such as file, stream, and device operations. |
| ISA | Instruction set architecture: the instructions and capabilities admitted by a processor profile. |
| JIT | Just-in-time compilation: generating native code during program execution. |
| JSON | JavaScript Object Notation: a structured data format, not executable JavaScript. |
| LINQ | Language Integrated Query: .NET query operators and C# query syntax. |
| LRU | Least recently used: an eviction policy; pinned resources remain ineligible for eviction. |
| LTS / STS | Long-term support / standard-term support. Exact support periods follow the recorded .NET release policy. |
| MTP | `Microsoft.Testing.Platform`: an optional runner only if the supplied NUnit foundation verifies its integration/options. |
| NUnit | The required engine test framework; runner/adapter selection is a separate build-infrastructure choice. |
| EBIN1 / EPKG2 / EIDX | Version-one engine binary records / version-two engine packages / their binary index; these are format identifiers defined in [10](10-binary-data-and-authoring-formats.md), not standard external formats. |
| DTD | Document type definition in XML; external entity/DTD processing is disabled for authoring adapters. |
| Morton key | Interleaved integer coordinate bits used for spatial ordering; three full 32-bit coordinates need 96 bits. |
| Dyadic / compensation | An exact integer times a power of two / a private high-low floating representation retaining rounded update residue; neither implies arbitrary-duration exact integration. |
| OOM | Out of memory. |
| OS | Operating system. |
| R2R | ReadyToRun: .NET precompiled managed assemblies that may still require JIT compilation for some code. |
| RC / RC1 | Release candidate / release candidate 1; not a synonym for GA. |
| RID | Runtime identifier, such as `win-x64`, selecting a deployment platform. |
| SBOM | Software bill of materials: the recorded components, versions, and licensing/provenance information in a build. |
| SDK | Software development kit; SDK selection and runtime selection are separate contracts. |
| SHA256 | SHA-256, the 256-bit Secure Hash Algorithm; a content digest is not proof of publisher authenticity. |
| SPSC | Single-producer/single-consumer: a queue with exactly one writer and one reader. |
| UTC | Coordinated Universal Time; wall-clock timestamps, not a monotonic elapsed-time source. |
| URI / MIME | Uniform resource identifier / Multipurpose Internet Mail Extensions; MIME media types describe transferred content. |
| UTF-8 / UTF-16 / UTF8 / UTF16 | Unicode Transformation Format with eight-bit / sixteen-bit code units. The unhyphenated spellings also occur in API names. |
| NFC | Normalization Form C: Unicode canonical decomposition followed by canonical composition. It is not applied automatically to user text. |
| XML / HTML / PDF | Extensible Markup Language / HyperText Markup Language / Portable Document Format. |
| HTTPS / HTTP / TLS | Hypertext Transfer Protocol Secure / Hypertext Transfer Protocol / Transport Layer Security. HTTPS carries HTTP over an encrypted TLS transport. |
| TRX | The XML test-result format emitted by the configured .NET test-report extension. |
| AVL | The balanced binary-search-tree family named after Adelson-Velsky and Landis. |
| SoA | Structure of arrays: separate columns for components/fields rather than one array of complete objects. |
| Lease | A bounded right to use storage or a version. A view does not own its backing storage; ownership and lease completion must be explicit. |
| Epoch / generation | An epoch identifies a replacement lifetime, such as a recreated device; a generation distinguishes successive occupants of a recycled slot. |
| Archetype | An entity storage group with the same registered component set. |
| ECS | Entity-component-system: entities identify objects, components hold data, and systems perform operations over matching data. |
| Headless / offscreen | Headless opens no native display/GPU/device. Offscreen native rendering still uses a real graphics device but has no visible presentation surface. |
| Fault / rejection / absence | An unexpected failure / an expected domain-specific refusal / an expected missing value. These use the distinct exception, response, and `Try` contracts in chapter [02](02-dotnet-api-memory-and-ownership.md). |

## 3. Geometry, rendering, and graphics (informative)

| Term | Expansion / meaning |
|---|---|
| CPU / GPU | Central processing unit / graphics processing unit. |
| LH / RH | Left-handed / right-handed coordinate convention. Neither determines vector multiplication order or depth-buffer direction. |
| CW / CCW | Clockwise / counterclockwise winding or rotation; the viewing plane and direction must also be specified. |
| SRT / MVP | Scale-rotation-translation / model-view-projection. In this engine's row-vector notation, a local matrix is `S * R * T` and a vertex becomes `position * M * V * P`. |
| Homogeneous position | A point represented as `(x, y, z, 1)`. The fourth component allows translation to participate in matrix multiplication. A direction uses `W = 0`. |
| Clip space / NDC | Projection output before division by `W` / normalized device coordinates after that division. Clip-space and NDC bounds are not interchangeable before division. |
| FOV | Field of view; camera angles are in radians. |
| Inverse transpose | The matrix used to transform surface normals under a general invertible linear transform, including nonuniform scale. |
| Polar / axial | Ordinary directional vectors such as position and force / orientation-dependent quantities such as angular velocity and torque. They transform differently under a reflection. |
| Shear | A transform that skews axes; a composed hierarchy can introduce it even when each local pose contains only scale, rotation, and translation. |
| Reversed Z | Near depth is 1 and far depth is 0; this is independent of the world's forward axis. |
| binary32 / binary64 / IEEE 754 | The IEEE 754 floating-point formats used for `float` / `double`. IEEE is the Institute of Electrical and Electronics Engineers. |
| FP32 / FP64 | Thirty-two-bit / sixty-four-bit floating-point arithmetic, ordinarily IEEE binary32 / binary64 in these specifications; neither implies a global-coordinate representation. |
| NaN / ULP | Not a Number / unit in the last place: a spacing measure for adjacent representable floating-point values at a given magnitude. |
| SIMD | Single instruction, multiple data: one instruction operates on several data lanes. |
| FMA | Fused multiply-add: multiplication and addition with one final rounding rather than two separate roundings. |
| D3D12 / DX / DXGI | Direct3D 12 / DirectX / DirectX Graphics Infrastructure; D3D12 is the graphics API and DXGI owns related adapter/presentation facilities on Windows. |
| FL / SM | Feature level / shader model, as in D3D12 feature level `12_0` and shader model `6.0`. |
| RHI | Rendering hardware interface: the engine's portable graphics-device/resource boundary. |
| WSI | Window-system integration: native surfaces and presentation integration for Vulkan. |
| KHR / EXT | Vulkan extension-name categories: Khronos extensions / multi-vendor extensions. The advertised extension and feature still must be enabled. |
| HLSL / DXC / DXIL | High-Level Shading Language / DirectX Shader Compiler / DirectX Intermediate Language. |
| SPIR-V | Khronos's standardized shader intermediate representation, used by the Vulkan backend; SPIR means Standard Portable Intermediate Representation. |
| VS / PS / CS | Vertex shader / pixel shader / compute shader; stage/target labels, not C# language versions. |
| UAV / RTV | Unordered-access view / render-target view: D3D resource views with different access roles. |
| RAW / WAR / WAW | Read-after-write / write-after-read / write-after-write dependencies; identify which accesses must be ordered. |
| FIFO | First in, first out; Vulkan's required default presentation mode. |
| Swapchain / fence / barrier | Presentable image sequence / completion marker / ordering, visibility, or state-transition operation. A GPU submission fence is not automatically proof of presentation completion. |
| HDR / SDR | High dynamic range / standard dynamic range. |
| RGB / RGBA / BGRA | Red-green-blue / red-green-blue-alpha / blue-green-red-alpha component ordering. |
| UNorm / sRGB | Unsigned normalized values / the standardized nonlinear RGB encoding. A format's color encoding is distinct from its component order. |
| UV | Conventional texture-coordinate names `U` and `V`, not world axes or a required acronym expansion. |
| PBR / BRDF | Physically based rendering / bidirectional reflectance distribution function: the material model / a function describing reflected light by incoming and outgoing direction. |
| GGX / LUT | The Trowbridge-Reitz microfacet distribution used here / lookup table. |
| LOD | Level of detail: a selected representation based on projected size, not a physics-topology change. |
| PCF | Percentage-closer filtering: combining multiple shadow comparison samples. |
| MSAA / FXAA / TAA | Multisample anti-aliasing / fast approximate anti-aliasing / temporal anti-aliasing; excluded from the first renderer. |
| FPS | Frames per second. |
| glTF / GLB | GL Transmission Format / its binary container; a source format converted at import, not the engine's coordinate authority. |
| PNG / JPEG / ICC | Portable Network Graphics / Joint Photographic Experts Group image format / International Color Consortium color profiles. |
| DRED | Device Removed Extended Data: D3D12 device-loss diagnostics. |
| AVX2 / SSE3 / SSSE3 / SSE4 | Advanced Vector Extensions 2 / Streaming SIMD Extensions 3 / Supplemental Streaming SIMD Extensions 3 / Streaming SIMD Extensions 4. Admission follows the exact processor profile, not this list alone. |
| POPCNT / CX16 | Population-count instruction capability / sixteen-byte compare-exchange capability used in x86 processor-profile descriptions. |
| VR / RAM / SSD | Virtual reality / random-access memory / solid-state drive. |

## 4. Physics and numerical algorithms (informative)

| Term | Expansion / meaning |
|---|---|
| AABB / OBB | Axis-aligned bounding box / oriented bounding box. |
| BVH | Bounding volume hierarchy: a tree of enclosing bounds that reduces candidate geometry tests. |
| CCD / TOI | Continuous collision detection / time of impact: detecting intersections over motion / the selected impact time or fraction. |
| SAT | Separating axis theorem: disjoint projected intervals provide a nonintersection certificate for the supported convex geometry. |
| GJK / EPA | Gilbert-Johnson-Keerthi / expanding polytope algorithm. They are discussed as alternatives, not used by this profile's finite SAT/feature enumeration. |
| LCP / SPD | Linear complementarity problem / symmetric positive definite matrix. |
| NGS | Nonlinear Gauss-Seidel: iterative position-constraint correction. |
| RMS | Root mean square: square root of the mean squared observations. |
| Broad phase / narrow phase | Candidate-pair generation using bounds / detailed geometry testing of those candidates. |
| Manifold / contact feature | A bounded contact-point set for a collider pair / a stable vertex, edge, face, or mesh-patch identity. |
| Warm start | Reusing admitted previous constraint impulses as the next solve's initial estimate; it is part of strict replay state. |
| Slop / hysteresis | Deliberate tolerance before corrective response / distinct entry and exit thresholds to prevent boundary chatter. |
| Shape cooking | Validating and deriving immutable collision data from authoring geometry; it does not mean mesh rendering or unrestricted hull generation. |
| Dyadic value | A value expressible as an integer times a power of two. Every finite binary32 component has such an exact representation. |
| PCG / PCG32 / XSH-RR / PRNG / RNG | Permuted congruential generator / its specified 32-bit output family / xorshift-high, random-rotate output permutation / pseudorandom number generator / random number generator. Chapter [27](27-state-serialization-replay-and-recovery.md) defines the exact transition and state. |

## 5. Platform, audio, input, and text (informative)

| Term | Expansion / meaning |
|---|---|
| DPI / DIP | Dots per inch / device-independent pixel. Logical UI dimensions are converted through the window's scale to physical pixels. |
| UI | User interface. |
| WASAPI / ALSA | Windows Audio Session API / Advanced Linux Sound Architecture. This engine's chosen Linux output is PipeWire, not an automatic ALSA fallback. |
| DSP | Digital signal processing: audio filtering, mixing, and conversion algorithms. |
| PCM / PCM16 / DAC | Pulse-code modulation / signed sixteen-bit PCM in the stated file/device contract / digital-to-analog converter. |
| WAV / WAVE / RIFF | Waveform Audio File Format / its format label / Resource Interchange File Format, the containing chunk format. |
| HRTF / LFE / MIDI | Head-related transfer function / low-frequency-effects channel / Musical Instrument Digital Interface. |
| STA / MTA | Single-threaded apartment / multithreaded apartment: Windows COM execution models, not physics centers of mass. |
| HRESULT / HWND / XID | Windows status-code type / native Windows window handle / X11 resource identifier. These are native names, not public engine handle types. |
| TSF / IMM / IMM32 / IME | Text Services Framework / Input Method Manager / its Windows API family / input method editor. Composition is not a stream of synthesized physical key presses. |
| ACP | Application character position in the TSF text-store contract; not an ANSI code page in this context. |
| HID / USB | Human Interface Device / Universal Serial Bus. |
| XKB / XIM / XIC | X Keyboard Extension / X Input Method / X Input Context. |
| EWMH / ICCCM | Extended Window Manager Hints / Inter-Client Communication Conventions Manual: X11 window-manager protocols. |
| WIC | Windows Imaging Component: the selected Windows image-decoding foundation. |
| ICU / CJK / RTL / LCD | International Components for Unicode / Chinese-Japanese-Korean scripts / right-to-left text / liquid-crystal display. LCD subpixel glyph rendering is not part of the core renderer. |
| Grapheme / bidi / shaping | A user-perceived text cluster / bidirectional text ordering / mapping text and font features to positioned glyphs. They are separate from physical key input. |
| SDL / GLFW | Simple DirectMedia Layer / the GLFW native window/input library. They are examples of excluded substitute windowing frameworks, not engine dependencies. |
| GPL | GNU General Public License; dependency-license admission remains the build policy's responsibility. |
| RFC | Request for Comments: the numbered technical-publication series, including the cited identifier/data conventions. |
| NUL | The zero code unit/byte terminator, distinct from the C# `null` reference. |

API spellings, shader semantics such as `POSITION`, native constants such as `VK_FRONT_FACE_CLOCKWISE`, register labels, format names, and local formula variables retain their exact spelling. Their owning chapters explain their use; they are not expanded by guessing at uppercase letters.
